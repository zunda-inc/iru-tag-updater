# 001 — Iru タグアップデーター 実装プラン

## 1. 目的

Prism で条件に合致するデバイスを検索し、まだタグが付いていないデバイスに Iru (旧 Kandji) の
タグを付与するバッチジョブ。Cloud Run Jobs 上で Cloud Scheduler により1時間に1回実行する。

- タグ付与は **追加のみ**（条件から外れてもタグは剥がさない）
- 対象条件・タグは `config.json` で宣言的に定義する

---

## 2. 確認済み API 仕様（`docs/api-docs-postman.json` より）

### 2.1 Prism 検索
```
GET /api/v1/prism/{category}?filter={URLエンコードしたJSON}&limit=&cursor=
Authorization: Bearer {api_token}
```
- `category` は `apps`, `certificates`, `device_information`, `filevault` などの Prism カテゴリ。
  config.json の `query` がそのまま `category` になる。
- `filter` は JSON オブジェクト（例 `{"bundle_id":{"in":["com.apple.Home"]}}`）を
  文字列化し URL エンコードして渡す。演算子は `in` / `not_in` など。
- レスポンス:
  ```json
  { "offset": 0, "limit": 25, "total": 165,
    "data": [ { "device_id": "...", "tags": null, "serial_number": "...",
                "device__name": "...", "bundle_id": "...", ... } ],
    "cursor": "base64..." }
  ```
- **どのカテゴリの行にもデバイス共通列（`device_id`, `tags`, `serial_number`, `device__name`）が含まれる。**
  → 条件マッチと現在のタグ状態を Prism のレスポンスだけで判定できる。
- ページネーション: `cursor` が非 null の間、`cursor=` を付けて再取得。上限 300 件/リクエスト。
- レート制限: 50 req/sec、10000 req/hour（`Ratelimit-*` ヘッダ、超過時 429）。

### 2.2 タグ更新
```
PATCH /api/v1/devices/{device_id}
Authorization: Bearer {api_token}
Content-Type: application/json

{ "tags": ["tag1", "tag2", ...] }
```
- **`tags` は配列全体の置換**（差分追加ではない）。
  → 既存タグを保持するため、必ず「現在のタグ ∪ 付与するタグ」を送る。
- レスポンスは更新後のデバイスレコード全体（`tags` を含む）。

### 2.3 タグ一覧
```
GET /api/v1/tags
Authorization: Bearer {api_token}
```
- レスポンス（DRF 形式のページネーション）:
  ```json
  { "count": 2, "next": null, "previous": null,
    "results": [ { "id": "71dd53f1-...", "name": "accuhive_01" },
                 { "id": "4fe23c39-...", "name": "accuhive_02" } ] }
  ```
- `?search=` で名前部分一致検索も可能だが、本ツールは全件取得して名前→ID の対応表を作る。
- `next` が非 null の間ページを辿る。
- **用途**: config の `tag`（名前）を実際のタグ ID に解決する（§4 手順0）。
  デバイスへの PATCH の `tags` に名前と ID のどちらが必要かは環境により異なる可能性があるため、
  起動時に必ず ID を解決して保持しておく。

---

## 3. config.json スキーマ（確定版）

CLAUDE.md の例は JSON として壊れている箇所（certificates の filter）があるため、下記に正規化する。

```json
{
  "endpoint": "https://example.api.kandji.io",
  "updates": [
    {
      "tag": "Iru Installed",
      "conditions": [
        { "query": "apps",         "filter": { "bundle_id":   { "in": ["io.kandji.Self-Service-Mobile"] } } },
        { "query": "certificates", "filter": { "common_name": { "in": ["MDM SCEP VERIFIER ..."] } } }
      ]
    }
  ]
}
```

- `endpoint`: Iru API のベース URL（末尾 `/api/v1/...` はコード側で付与）。
- `updates[].tag`: 付与するタグ名。
- `updates[].conditions[]`: 各要素が 1 つの Prism クエリ。
  - `query`: Prism カテゴリ名（= URL パス）。
  - `filter`: Prism にそのまま渡す不透明な JSON（アプリ側は解釈せず素通し）。
- **複数 `conditions` は AND（積集合）** として扱う。
  Iru セルフサービスアプリがある **かつ** 対象証明書がある」デバイスにのみ付与。
  → この AND 解釈が本プランの前提（§8 で要確認）。

---

## 4. コアアルゴリズム

**手順0（起動時・全 update 共通）: タグ名 → タグ ID の解決**
- `GET /api/v1/tags` を全ページ取得し、`name → id` の対応表を作る。
- config の各 `updates[].tag`（名前）を ID に解決する。
- **1 つでも対応する ID が取得できなかったタグがあれば、例外を投げて終了**（非 0 の終了コード）。
  → 存在しないタグ名を config に書いた場合に早期失敗させる。
- 解決した ID は以降のタグ付与（PATCH）で使用する。

各 `update` について:

1. **条件ごとに Prism を検索**
   `GET /api/v1/prism/{query}?filter={filter}` をページネーションしながら全件取得。
   各条件について `device_id → tags[]` のマップを作る。
2. **積集合を取る**
   全条件に登場した `device_id` の集合（= すべての条件を満たすデバイス）。
3. **未タグのデバイスを抽出**
   Prism 行の `tags` に対象タグが含まれていないデバイスだけ残す。
   （`tags` はデバイス単位属性なので Prism レスポンスから判定可能）
4. **タグ付与**
   対象デバイスごとに `PATCH /api/v1/devices/{id}` で `tags = 既存 ∪ {対象タグ}` を送信
   （対象タグは手順0で解決した識別子を使用）。
   - 競合を避けるため PATCH 直前に `GET /api/v1/devices/{id}` で最新タグを取得し和集合を作る
     （新規付与対象は通常少数のため追加コストは軽微）。
5. **剥がさない**: 条件から外れたデバイスへの処理は行わない（CLAUDE.md 方針どおり）。

冪等性: 既にタグ済みは §3 でスキップされるため、再実行しても重複付与や無駄な PATCH は発生しない。

---

## 5. 技術スタック / プロジェクト構成

- **.NET 10.0 / C#** のコンソールワーカー（`Microsoft.Extensions.Hosting` の Generic Host）。
  Cloud Run **Jobs** は起動して完走・終了するバッチなので、Web サーバではなく
  「実行して終了」するワーカーを採用（実行後に終了コードで成否を返す）。
  ※ CLAUDE.md には「ASP.NET Core 10.0」とあるが、Jobs 用途ではコンソールワーカーが適切。
    Web ホスト希望であれば §8 で確認。

```
iru-tag-updater/
├── src/IruTagUpdater/
│   ├── IruTagUpdater.csproj
│   ├── Program.cs                     # Host 構築・DI・実行・終了コード
│   ├── Configuration/
│   │   ├── AppConfig.cs               # AppConfig / TagUpdate / Condition レコード
│   │   ├── ConfigLoader.cs            # config.json ファイル優先 → Base64 env → 無ければ例外
│   │   └── TokenProvider.cs           # .env(IRU_API_TOKEN) 優先 → env → 無ければ例外
│   ├── Kandji/
│   │   ├── IKandjiClient.cs
│   │   ├── KandjiClient.cs            # Prism 検索(ページング)・ListTags・GetDevice・PatchTags
│   │   ├── PrismResponse.cs           # { offset,limit,total,data,cursor }
│   │   ├── TagListResponse.cs         # { count,next,previous,results:[{id,name}] }
│   │   └── DeviceTagInfo.cs           # device_id, tags 抽出用
│   ├── TagResolver.cs                 # 手順0: タグ名→ID 解決（未解決なら例外）
│   └── TagUpdateService.cs            # §4 のアルゴリズム
├── tests/IruTagUpdater.Tests/
│   ├── ConfigLoaderTests.cs
│   ├── TagUpdateServiceTests.cs       # 積集合・未タグ抽出・和集合ロジック
│   └── KandjiClientTests.cs           # HttpMessageHandler モックで検索/PATCH検証
├── Dockerfile
├── deploy/
│   ├── job.yaml                       # Cloud Run Job 定義（or gcloud コマンド）
│   └── deploy.md                      # デプロイ手順（secret, scheduler 含む）
├── config.example.json                # config.json の雛形（config.json 自体は .gitignore）
├── .env.example                       # .env の雛形（.env 自体は .gitignore）
├── .dockerignore                      # config.json / .env を除外
├── .gitignore                         # config.json / .env を除外
└── README.md
```

### 設定・トークンの読み込み（開発時 vs デプロイ時）

**config の読み込み優先順位**（`ConfigLoader`）:
1. ルートディレクトリの `config.json` ファイルが存在すれば、それを直接読む（開発時）。
2. なければ環境変数 `IRU_CONFIG_BASE64`（Base64 エンコードした config.json）を読む（Cloud Run Jobs）。
3. どちらも取得できなければ**例外を投げて終了**（非 0 の終了コード）。

**API トークンの読み込み優先順位**（`TokenProvider`）:
1. ルートの `.env` ファイルから `IRU_API_TOKEN` を読む（開発時）。
2. なければ環境変数 `IRU_API_TOKEN` を読む（Secret Manager → Jobs の env）。
3. どちらも取得できなければ**例外を投げて終了**（非 0 の終了コード）。

- `.env` の読み込みには `DotNetEnv` パッケージを使う（`.env` があれば環境変数に流し込む）。
- `config.json` と `.env` は **`.gitignore` / `.dockerignore` に追加**し、リポジトリ・イメージに含めない。
  リポジトリには `config.example.json` / `.env.example` を置く。

### その他の環境変数
- `DRY_RUN`（任意, 既定 false）: true のとき PATCH を実行せずログのみ。初回検証用。

### HTTP クライアント
- `IHttpClientFactory` の typed client。ベース URL は `endpoint`、`Authorization` ヘッダ既定付与。
- **リトライ/レート制御**: 429 と 5xx に対し指数バックオフ（`Ratelimit-Reset` / `Retry-After` 尊重）。
  Polly を導入（`Microsoft.Extensions.Http.Resilience`）。50 req/sec を超えないよう軽い間隔制御も入れる。

### ログ
- 構造化ログ（`ILogger`）。付与したデバイス（serial/device_id）、スキップ数、エラーを出力。
- Cloud Logging にそのまま流れる。

---

## 6. インフラ / デプロイ

- **Dockerfile**: マルチステージ（`sdk:10.0` でビルド → `runtime:10.0`（または `runtime-deps` + self-contained）で実行）。
- **Artifact Registry** にプッシュ → **Cloud Run Job** を作成。
  - config は `--set-env-vars IRU_CONFIG_BASE64=...`
  - API キーは `--set-secrets IRU_API_TOKEN=iru-api-token:latest`（Secret Manager）
- **Cloud Scheduler**: 1時間ごとに Job を起動（Cloud Run Jobs run を叩く HTTP or Scheduler→Run 連携）。
- 手順とコマンドは `deploy/deploy.md` にまとめる（実際の GCP 実行は行わず雛形提供）。

---

## 7. 実装ステップ（着手順）

1. プロジェクト雛形 + csproj + `.gitignore` / `.dockerignore`（`config.json` / `.env` 除外）+ `config.example.json` / `.env.example`
2. `AppConfig` モデル、`ConfigLoader`（ファイル→env フォールバック）、`TokenProvider`（.env→env フォールバック、DotNetEnv）+ 単体テスト
3. `KandjiClient`: Prism 検索（カーソルページング）+ 単体テスト（HttpMessageHandler モック）
4. `KandjiClient`: `ListTags`（DRF ページング）/ `GetDevice` / `PatchTags` + テスト
5. `TagResolver`: 起動時にタグ名→ID 解決、未解決なら例外 + テスト
6. `TagUpdateService`: 積集合・未タグ抽出・和集合付与 + テスト（DRY_RUN 対応）
7. `Program.cs`: Host 構築・DI 配線・終了コード・ログ（手順0→各 update）
8. Dockerfile + ローカルビルド確認（`dotnet test`, `docker build`）
9. `deploy/deploy.md` + `job.yaml`（GCP 手順）
10. README

各ステップでビルド＆テストを通しながら進める。

---

## 8. 確定した方針（ユーザー確認済み）

1. **複数 conditions の論理**: ✅ AND（すべて満たす＝積集合）。
2. **アプリ形態**: ✅ コンソールワーカー（Generic Host）。ASP.NET Core Web ではない。
3. **環境変数名**: `IRU_CONFIG_BASE64` / `IRU_API_TOKEN`（既定のまま採用）。
4. **タグ削除**: ✅ 付与のみ。条件から外れてもタグは剥がさない。
