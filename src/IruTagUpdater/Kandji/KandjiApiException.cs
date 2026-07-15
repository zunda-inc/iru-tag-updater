namespace IruTagUpdater.Kandji;

/// <summary>Iru API 呼び出しが失敗したことを示す。</summary>
public sealed class KandjiApiException(string message) : Exception(message);
