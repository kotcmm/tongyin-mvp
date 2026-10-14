namespace Tongyin.Api.Core;

/// <summary>
/// 后端运行环境和外部服务配置。
/// </summary>
public sealed record Settings
{
    /// <summary>
    /// 服务绑定地址。
    /// </summary>
    public string ListenAddr { get; init; } = ":18080";
    /// <summary>
    /// 服务公开访问地址。
    /// </summary>
    public string PublicUrl { get; init; } = "http://localhost:18080";
    /// <summary>
    /// 服务数据保存路径。
    /// </summary>
    public string DataDir { get; init; } = "./data";
    /// <summary>
    /// 数据库驱动类型。
    /// </summary>
    public string DBDriver { get; init; } = "file";
    /// <summary>
    /// 数据库连接字符串。
    /// </summary>
    public string DatabaseUrl { get; init; } = "";
    /// <summary>
    /// 用于会话签名的密钥。
    /// </summary>
    public string SessionSecret { get; init; } = "";
    /// <summary>
    /// 兼容旧管理系统的管理员令牌。
    /// </summary>
    public string AdminToken { get; init; } = "";
    /// <summary>
    /// 默认管理员用户名。
    /// </summary>
    public string AdminUsername { get; init; } = "admin";
    /// <summary>
    /// 首次启动的管理员密码。
    /// </summary>
    public string AdminPassword { get; init; } = "";
    /// <summary>
    /// 微信小程序应用标识。
    /// </summary>
    public string WxAppId { get; init; } = "";
    /// <summary>
    /// 微信小程序应用密钥。
    /// </summary>
    public string WxAppSecret { get; init; } = "";
    /// <summary>
    /// AI（人工智能）任务服务地址。
    /// </summary>
    public string AiWorkerUrl { get; init; } = "";
    /// <summary>
    /// AI 任务访问令牌。
    /// </summary>
    public string AiWorkerToken { get; init; } = "";
    /// <summary>
    /// AI 回调鉴权令牌。
    /// </summary>
    public string AiWebhookToken { get; init; } = "";
    /// <summary>
    /// 微信支付商户标识。
    /// </summary>
    public string WxMchId { get; init; } = "";
    /// <summary>
    /// 微信支付商户证书序列号。
    /// </summary>
    public string WxMchSerial { get; init; } = "";
    /// <summary>
    /// 微信支付商户私钥路径。
    /// </summary>
    public string WxMchPrivateKeyPath { get; init; } = "";
    /// <summary>
    /// 微信支付平台公钥路径。
    /// </summary>
    public string WxPlatformPublicKeyPath { get; init; } = "";
    /// <summary>
    /// 微信支付平台证书序列号。
    /// </summary>
    public string WxPlatformSerial { get; init; } = "";
    /// <summary>
    /// 微信支付接口密钥。
    /// </summary>
    public string WxPayApiKey { get; init; } = "";
    /// <summary>
    /// 微信支付通知回调地址。
    /// </summary>
    public string WxPayNotifyUrl { get; init; } = "";
    /// <summary>
    /// 快递服务 API（应用接口）密钥。
    /// </summary>
    public string Kuaidi100Key { get; init; } = "";
    /// <summary>
    /// 快递服务客户标识。
    /// </summary>
    public string Kuaidi100Customer { get; init; } = "";
    /// <summary>
    /// 商品价格，以人民币分为单位。
    /// </summary>
    public int PriceFen { get; init; } = 12900;
    /// <summary>
    /// 允许的免费生成次数。
    /// </summary>
    public int FreeImageAttempts { get; init; } = 2;
    /// <summary>
    /// 是否开启开发模式。
    /// </summary>
    public bool DevMode { get; init; } = false;

    private static string Env(string key, string fallback = "") =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)) ? fallback : Environment.GetEnvironmentVariable(key)!.Trim();
    private static int IntEnv(string key, int fallback) => int.TryParse(Env(key), out var n) && n > 0 ? n : fallback;

    /// <summary>
    /// 从环境变量读取配置。
    /// </summary>
    public static Settings Load() => new()
    {
        ListenAddr = Env("LISTEN_ADDR", ":18080"),
        PublicUrl = Env("PUBLIC_URL", "http://localhost:18080").TrimEnd('/'),
        DataDir = Env("DATA_DIR", "./data"),
        DBDriver = Env("DB_DRIVER", "file").ToLowerInvariant(),
        DatabaseUrl = Env("DATABASE_URL"),
        SessionSecret = Env("SESSION_SECRET"),
        AdminToken = Env("ADMIN_TOKEN"),
        AdminUsername = Env("ADMIN_BOOTSTRAP_USER", "admin"),
        AdminPassword = Env("ADMIN_BOOTSTRAP_PASSWORD"),
        WxAppId = Env("WX_APP_ID"),
        WxAppSecret = Env("WX_APP_SECRET"),
        AiWorkerUrl = Env("AI_WORKER_URL"),
        AiWorkerToken = Env("AI_WORKER_TOKEN"),
        AiWebhookToken = Env("AI_WEBHOOK_TOKEN"),
        WxMchId = Env("WX_MCH_ID"),
        WxMchSerial = Env("WX_MCH_SERIAL"),
        WxMchPrivateKeyPath = Env("WX_MCH_PRIVATE_KEY"),
        WxPlatformPublicKeyPath = Env("WX_PLATFORM_PUBLIC_KEY"),
        WxPlatformSerial = Env("WX_PLATFORM_SERIAL"),
        WxPayApiKey = Env("WX_PAY_API_V3_KEY"),
        WxPayNotifyUrl = Env("WX_PAY_NOTIFY_URL"),
        Kuaidi100Key = Env("KUAIDI100_KEY"),
        Kuaidi100Customer = Env("KUAIDI100_CUSTOMER"),
        PriceFen = IntEnv("PRODUCT_PRICE_FEN", 12900),
        FreeImageAttempts = IntEnv("FREE_IMAGE_ATTEMPTS", 2),
        DevMode = Env("DEV_MODE") == "true"
    };
    /// <summary>
    /// 检查必须的运行配置是否完整。
    /// </summary>
    public void Validate()
    {
        if (SessionSecret.Length < 24) throw new InvalidOperationException("SESSION_SECRET 至少需要24位");
        if (DBDriver is not ("file" or "postgres" or "mysql")) throw new InvalidOperationException("DB_DRIVER 不支持");
        if (DBDriver == "file" && !DevMode) throw new InvalidOperationException("file 存储仅允许开发模式");
        if (DBDriver != "file" && DatabaseUrl.Length == 0) throw new InvalidOperationException("数据库连接信息未配置");
        if (!DevMode && (!PublicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                         WxAppId == "" || WxAppSecret == ""))
            throw new InvalidOperationException("生产环境需要 HTTPS PUBLIC_URL 和微信小程序配置");
    }
    /// <summary>
    /// 构造当前服务监听地址。
    /// </summary>
    public string ListenUrl() => "http://0.0.0.0:" + ListenAddr[(ListenAddr.LastIndexOf(':') + 1)..];
    /// <summary>
    /// 提供IsNullOrEmpty所需的数据或操作。
    /// </summary>
    public bool PayReady => !string.IsNullOrEmpty(WxAppId) && !string.IsNullOrEmpty(WxMchId) &&
                            !string.IsNullOrEmpty(WxMchSerial) && !string.IsNullOrEmpty(WxMchPrivateKeyPath) &&
                            !string.IsNullOrEmpty(WxPlatformPublicKeyPath) && !string.IsNullOrEmpty(WxPayApiKey) &&
                            !string.IsNullOrEmpty(WxPayNotifyUrl);
}
