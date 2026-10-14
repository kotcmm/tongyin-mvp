namespace Tongyin.Domain.Works;

// Values map to the legacy Go wire format in Infrastructure, not here.

/// <summary>
/// 作品生命周期状态。
/// </summary>
public enum WorkStatus { ImagePending, ImageReady, ModelPending, ModelReady, Failed, Unknown }
