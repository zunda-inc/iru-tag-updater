# 002 — GitHub Actions で GHCR に Docker イメージを push

## 1. 目的

001 の Dockerfile を使い、**GitHub Actions** でコンテナイメージをビルドして
**GHCR (GitHub Container Registry, `ghcr.io`)** に push する CI を用意する。

デプロイ側（Cloud Run Jobs 等）は、ここで push したイメージ（タグ or ダイジェスト）を参照する。

---

## 2. 前提

- GitHub リポジトリ: **`zunda-inc/iru-tag-updater`**（`origin` 接続済み、`main` は `origin/main` に追従）。
  → イメージ名は **`ghcr.io/zunda-inc/iru-tag-updater`**（すべて小文字、GHCR の要件を満たす）。
- 認証は Actions 組み込みの `GITHUB_TOKEN` を使う（追加のシークレット登録は不要）。
  GHCR への push は `packages: write` 権限で可能。
- GHCR パッケージを org `zunda-inc` 配下に作るため、org の
  「Package creation」ポリシーで Actions からの作成が許可されている必要がある（§6 で確認）。

---

## 3. ワークフロー設計

ファイル: `.github/workflows/docker-publish.yml`

### トリガー（案）
- `push` の `main` ブランチ → `latest` と `sha` タグで push
- `push` のタグ `v*`（例 `v1.2.3`）→ semver タグで push
- `pull_request` → **ビルドのみ（push しない）** で壊れていないか確認
- `workflow_dispatch` → 手動実行

### ジョブ構成
1. **test**: `dotnet test`（.NET 10）。失敗したら以降を止める（壊れたイメージを push しない）。
2. **build-and-push**: test 成功後に実行。
   - `docker/login-action` で `ghcr.io` にログイン（`github.actor` / `GITHUB_TOKEN`）
   - `docker/metadata-action` でタグ・ラベルを生成
   - `docker/build-push-action` でビルド＆push（PR 時は `push: false`）
   - buildx の GHA キャッシュ（`cache-from/to: type=gha`）でビルド高速化

### イメージ名・タグ
- イメージ名: `ghcr.io/<owner>/iru-tag-updater`（GHCR は**小文字必須**）
- `docker/metadata-action` によるタグ:
  - `type=raw,value=latest`（default ブランチのみ）
  - `type=sha`（`sha-xxxxxxx` 短縮 SHA。デプロイでの固定参照に有用）
  - `type=semver,pattern={{version}}` / `{{major}}.{{minor}}`（`v*` タグ時）
  - `type=ref,event=pr`（PR ビルド識別用）

### プラットフォーム
- **`linux/amd64`** を基本とする（Cloud Run Jobs は amd64）。
- 必要なら `linux/arm64` も追加してマルチアーキ化可能（ビルド時間は増える）。→ §6 で確認。

### 権限
```yaml
permissions:
  contents: read
  packages: write
```

### ワークフロー雛形（イメージ）
```yaml
name: Docker publish (GHCR)

on:
  push:
    branches: [main]
    tags: ["v*"]
  pull_request:
  workflow_dispatch:

env:
  IMAGE_NAME: ghcr.io/${{ github.repository }}   # = ghcr.io/zunda-inc/iru-tag-updater (小文字)

jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet test -c Release

  build-and-push:
    needs: test
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write
    steps:
      - uses: actions/checkout@v4
      - uses: docker/setup-buildx-action@v3
      - name: Login to GHCR
        if: github.event_name != 'pull_request'
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.IMAGE_NAME }}
          tags: |
            type=raw,value=latest,enable={{is_default_branch}}
            type=sha
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=ref,event=pr
      - uses: docker/build-push-action@v6
        with:
          context: .
          platforms: linux/amd64
          push: ${{ github.event_name != 'pull_request' }}
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=gha
          cache-to: type=gha,mode=max
```

---

## 4. 補足

- GHCR のパッケージは既定で **private**。公開したい場合は Actions 初回 push 後に
  パッケージ設定で visibility を public に変更（または org のポリシーで管理）。
- デプロイで**ダイジェスト固定**したい場合は、`build-push-action` の
  `outputs.digest` をジョブサマリに出力しておくと参照が楽。
- 既存の `.dockerignore` により `.env` / `config.json` / `tests/` 等は既にビルドコンテキストから除外済み。

---

## 5. 作業ステップ

1. `.github/workflows/docker-publish.yml` を追加。
2. commit → push して Actions の実行を確認（test → build-and-push）。
3. GHCR (`zunda-inc/iru-tag-updater`) にイメージが出ることを確認。必要なら可視性を調整。

---

## 6. 質問事項一覧（実装前に確定したい点）

1. **GHCR パッケージの可視性**は public 
2. トリガーは案（main push / `v*` タグ / PR ビルド / 手動）でよい。
   PR 時はビルドのみ（push しない）で問題ない
3. ビルド対象プラットフォームは **`linux/amd64` のみ**でよい
4. **`dotnet test` をゲート**にして、失敗時は push しない方針
5. タグ運用は **`latest` + `sha` + semver（`v*` タグ）** でよい
   リリースは git タグ（`vX.Y.Z`）で切る運用を想定してよい
