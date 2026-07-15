# デプロイ手順 (Cloud Run Jobs + Cloud Scheduler)

Iru タグアップデーターを Cloud Run Jobs として登録し、Cloud Scheduler で1時間ごとに実行する。

前提の環境変数（適宜置き換え）:

```bash
export PROJECT_ID="your-project"
export REGION="asia-northeast1"
export REPO="iru-tag-updater"          # Artifact Registry リポジトリ
export IMAGE="$REGION-docker.pkg.dev/$PROJECT_ID/$REPO/iru-tag-updater:latest"
export JOB="iru-tag-updater"
```

## 1. Artifact Registry とイメージ

```bash
gcloud artifacts repositories create "$REPO" \
  --repository-format=docker --location="$REGION" --project="$PROJECT_ID"

gcloud auth configure-docker "$REGION-docker.pkg.dev"

# リポジトリのルートで
docker build -t "$IMAGE" .
docker push "$IMAGE"
```

## 2. API キーを Secret Manager に登録

```bash
printf '%s' 'YOUR_IRU_API_TOKEN' | \
  gcloud secrets create iru-api-token --data-file=- --project="$PROJECT_ID"
# 更新する場合:
# printf '%s' 'NEW_TOKEN' | gcloud secrets versions add iru-api-token --data-file=-
```

## 3. config.json を Base64 化

```bash
export IRU_CONFIG_BASE64="$(base64 -i config.json | tr -d '\n')"
```

## 4. Cloud Run Job を作成

```bash
gcloud run jobs create "$JOB" \
  --image="$IMAGE" \
  --region="$REGION" \
  --project="$PROJECT_ID" \
  --max-retries=1 \
  --task-timeout=900s \
  --set-env-vars="IRU_CONFIG_BASE64=$IRU_CONFIG_BASE64" \
  --set-secrets="IRU_API_TOKEN=iru-api-token:latest"

# 更新 (config やイメージを変えたとき) は create を update に:
# gcloud run jobs update "$JOB" --image="$IMAGE" \
#   --set-env-vars="IRU_CONFIG_BASE64=$IRU_CONFIG_BASE64" ...
```

動作確認 (手動実行):

```bash
gcloud run jobs execute "$JOB" --region="$REGION" --project="$PROJECT_ID" --wait
```

初回は挙動確認のため `--set-env-vars` に `DRY_RUN=true` を加えて実行し、
ログで付与対象を確認してから外すとよい。

## 5. Cloud Scheduler で毎時実行

Scheduler から Cloud Run Jobs を起動する専用サービスアカウントを用意し、
`run.jobs.run` 権限（例: `roles/run.invoker` + Job 実行権限）を付与する。

```bash
export SA="iru-tag-updater-scheduler@$PROJECT_ID.iam.gserviceaccount.com"

gcloud iam service-accounts create iru-tag-updater-scheduler \
  --project="$PROJECT_ID"

gcloud run jobs add-iam-policy-binding "$JOB" \
  --region="$REGION" --project="$PROJECT_ID" \
  --member="serviceAccount:$SA" --role="roles/run.invoker"

gcloud scheduler jobs create http iru-tag-updater-hourly \
  --project="$PROJECT_ID" \
  --location="$REGION" \
  --schedule="0 * * * *" \
  --time-zone="Asia/Tokyo" \
  --uri="https://$REGION-run.googleapis.com/apis/run.googleapis.com/v1/namespaces/$PROJECT_ID/jobs/$JOB:run" \
  --http-method=POST \
  --oauth-service-account-email="$SA"
```

## 終了コード

| コード | 意味 |
|-------|------|
| 0 | 正常終了 |
| 1 | 設定エラー (config / トークン / タグ解決の失敗) |
| 2 | 実行時エラー (API 呼び出し失敗など) |

`--max-retries` により、一時的な失敗はジョブ側でリトライされる。
