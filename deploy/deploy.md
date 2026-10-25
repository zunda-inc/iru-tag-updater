# デプロイ (Cloud Run Jobs + Cloud Scheduler)

Iru タグアップデーターは Cloud Run Jobs として登録し、Cloud Scheduler で1時間ごとに実行する。
GCP のリソースはこのリポジトリではなく、環境（利用先）ごとの Terraform リポジトリで管理し、
Terraform Cloud から apply する。

## 全体の流れ

```
main への push / v* タグ
  → GitHub Actions (.github/workflows/docker-publish.yml)
  → ghcr.io/zunda-inc/iru-tag-updater:{latest, sha-<commit>, <version>}
  → Artifact Registry のリモートリポジトリ (ghcr.io の代理)
  → Cloud Run Job (イメージのタグは Terraform で固定)
  → Cloud Scheduler (毎時 0 分 / Asia/Tokyo)
```

| リソース | 管理場所 |
|---------|---------|
| Docker イメージ | このリポジトリの GitHub Actions が GHCR に push |
| Artifact Registry (リモートリポジトリ) / Cloud Run Job / Cloud Scheduler / Secret Manager / サービスアカウント | Terraform リポジトリ |
| config.json の内容 (`IRU_CONFIG_BASE64`) | Terraform の locals (base64 化して環境変数に渡す) |
| Iru の API トークン (`IRU_API_TOKEN`) | Terraform Cloud の変数 → Secret Manager |

## 新しいイメージを本番に出す

Cloud Run Job は `sha-<commit>` のタグでイメージを固定している。
main にマージしただけでは本番に反映されないため、Terraform 側のタグを書き換える。

1. このリポジトリで main にマージし、`Docker publish (GHCR)` のワークフローが成功したことを確かめる
2. push されたタグを確かめる（ワークフローの Summary にタグと digest が出る）
3. 必要なら .NET ランタイムの版を確かめる

   ```bash
   docker buildx imagetools inspect ghcr.io/zunda-inc/iru-tag-updater:sha-<commit> \
     --format '{{json .Image.Config.Env}}' | grep DOTNET_VERSION
   ```

4. Terraform リポジトリの `locals.tf` の `iru_tag_updater_image_tag` を `sha-<commit>` に書き換えて PR を出す
5. Terraform Cloud の plan で、Cloud Run Job のイメージだけが変わることを確かめてから apply する

### ベースイメージのセキュリティ更新

Dockerfile は `mcr.microsoft.com/dotnet/runtime:10.0` を使う。.NET のセキュリティリリース
(通常は毎月第2火曜) はイメージを作り直さないと入らず、作り直しても Terraform のタグを
書き換えるまで本番には届かない。リリースが出たら、イメージの再ビルドとタグの更新までを行う。

## 動作確認

```bash
gcloud run jobs execute iru-tag-updater --region=asia-northeast1 --project="$PROJECT_ID" --wait
```

新しい環境の初回は、Terraform の環境変数に `DRY_RUN=true` を加えて実行し、
ログで付与対象を確認してから外すとよい。

## ローカルでのイメージの確認

本番への経路ではないが、イメージを手元で確かめる場合:

```bash
docker build -t iru-tag-updater .
docker run --rm \
  -e IRU_CONFIG_BASE64="$(base64 -i config.json | tr -d '\n')" \
  -e IRU_API_TOKEN="$IRU_API_TOKEN" \
  -e DRY_RUN=true \
  iru-tag-updater
```

## 終了コード

| コード | 意味 |
|-------|------|
| 0 | 正常終了 |
| 1 | 設定エラー (config / トークン / タグ解決の失敗) |
| 2 | 実行時エラー (API 呼び出し失敗など) |

Cloud Run Job の `max_retries` により、一時的な失敗はジョブ側でリトライされる。
