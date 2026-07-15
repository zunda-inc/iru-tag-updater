# Iru タグアップデーター

## これは何？
- Iru (旧称 Kandji) のタグを更新するためのツールです。
- Prism で条件にあうデバイスを検索し、Iru のタグを更新します。
- 条件は config.json で定義されます

## config.json の例

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

## 実装方針
- Prism でタグがついてないデバイスを検索する
- Prism で条件にあうデバイスを検索する
- タグがまだついてなくて、新たに条件に合うデバイスが合った場合にはタグをつける
- Prism で条件にあうデバイスがなくなった場合にはタグを外す必要はない

## インフラ
- Cloud Run Jobs で実行できるようにする
- Cloud Scheduler で定期実行 (1時間に1回) する
- config.json は Cloud Run Jobs の環境変数に Base64 でエンコードして渡す
- Iru の API キーは Secret Manager で管理する

## 実装
- ASP .NET Core 10.0 で実装する

## 参考
- [Iru API](https://api-docs.iru.com/)
