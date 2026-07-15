# Iru タグアップデーター

Prism で条件に合致するデバイスを検索し、まだタグが付いていないデバイスに Iru (旧 Kandji) の
タグを付与するバッチジョブ。Cloud Run Jobs 上で Cloud Scheduler により1時間ごとに実行する想定。

- タグ付与は **追加のみ**（条件から外れてもタグは剥がさない）
- 対象条件・タグは `config.json` で宣言的に定義する
- 起動時にタグ一覧を取得し、config のタグ名を **タグ ID に解決**（解決できないタグがあれば起動失敗）

## 動作の流れ

1. config とトークンを読み込む（下記「設定」）。
2. `GET /api/v1/tags` でタグ一覧を取得し、config の各 `tag`（名前）を ID に解決する。
   1つでも解決できなければ**例外で終了**（終了コード 1）。
3. 各 `update` について、条件（`conditions`）ごとに Prism を検索し、
   **全条件を満たす（AND / 積集合）** デバイスを求める。
4. そのうち対象タグが未付与のデバイスにのみ、既存タグを保持したまま
   （`PATCH /api/v1/devices/{id}` で `tags = 既存 ∪ 対象タグ`）タグを付与する。

## 設定

### config（`updates` と `endpoint`）

優先順位:

1. リポジトリルートの `config.json`（開発時）
2. 環境変数 `IRU_CONFIG_BASE64`（config.json を Base64 化した文字列 / Cloud Run Jobs）
3. どちらも無ければ例外で終了

スキーマは [`config.example.json`](config.example.json) を参照。

```json
{
  "endpoint": "https://<subdomain>.api.kandji.io",
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

- `query`: Prism のカテゴリ名（`apps`, `certificates`, `device_information` など。URL パスにそのまま使う）。
- `filter`: Prism にそのまま渡す JSON。演算子は `in` / `not_in` など。

### API トークン

優先順位:

1. ルートの `.env` 内の `IRU_API_TOKEN`（開発時。[`.env.example`](.env.example) 参照）
2. 環境変数 `IRU_API_TOKEN`（Secret Manager → Cloud Run Jobs）
3. どちらも無ければ例外で終了

### その他

- `DRY_RUN=true`: PATCH を実行せず、付与対象をログ出力するだけ（初回検証用）。

> `config.json` と `.env` は `.gitignore` / `.dockerignore` 済みで、リポジトリ・イメージには含めない。

## ローカル実行

```bash
cp config.example.json config.json   # 編集
cp .env.example .env                  # IRU_API_TOKEN を設定

dotnet run --project src/IruTagUpdater

# 付与せず確認だけ
DRY_RUN=true dotnet run --project src/IruTagUpdater
```

## テスト

```bash
dotnet test
```

## デプロイ

Cloud Run Jobs + Cloud Scheduler の手順は [`deploy/deploy.md`](deploy/deploy.md) を参照。

## 終了コード

| コード | 意味 |
|-------|------|
| 0 | 正常終了 |
| 1 | 設定エラー（config / トークン / タグ解決の失敗） |
| 2 | 実行時エラー（API 呼び出し失敗など） |

## プロジェクト構成

```
src/IruTagUpdater/
  Program.cs                  Host 構築・DI・実行・終了コード
  Configuration/              AppConfig / ConfigLoader / TokenProvider
  Kandji/                     IKandjiClient / KandjiClient / 各レスポンスモデル
  TagResolver.cs              タグ名→ID 解決（未解決なら例外）
  TagUpdateService.cs         コアアルゴリズム（積集合・未タグ抽出・和集合付与）
tests/IruTagUpdater.Tests/    xUnit テスト
```
