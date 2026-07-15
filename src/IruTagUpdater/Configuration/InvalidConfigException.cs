namespace IruTagUpdater.Configuration;

/// <summary>設定 (config.json / トークン) の取得・検証に失敗したことを示す。捕捉されると非 0 終了する。</summary>
public sealed class InvalidConfigException(string message) : Exception(message);
