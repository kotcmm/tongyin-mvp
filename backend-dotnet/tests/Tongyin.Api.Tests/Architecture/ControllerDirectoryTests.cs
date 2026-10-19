using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Tongyin.Api.ViewModels.Works;
using Xunit;

namespace Tongyin.Api.Tests.Architecture;

/// <summary>保证小程序、管理后台、外部回调和公共媒体 Controller 的目录与命名空间分离。</summary>
public sealed class ControllerDirectoryTests
{
    /// <summary>全部 HTTP Controller 必须进入其所属边界目录，不允许残留在 Controllers 根目录。</summary>
    [Fact]
    public void Controllers_AreGroupedByClientBoundary()
    {
        var groups = new Dictionary<string, string[]>
        {
            ["Miniapp"] =
            [
                "MiniappConfigController", "OrderTrackingController", "OrdersController",
                "ShippingAddressController", "WechatAuthController", "WechatPaymentController",
                "WechatProfileController", "WorkOrderController", "WorksController"
            ],
            ["Admin"] =
            [
                "AdminAccountController", "AdminAiSettingsController", "AdminAiTasksController", "AdminOrdersController", "AdminWorksController",
                "WechatSystemSettingsController"
            ],
            ["Internal"] = ["WechatPayNotifyController"],
            ["Shared"] = ["MediaController"]
        };

        var actual = typeof(WorkResponse).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ControllerBase)) && !type.IsAbstract)
            .ToArray();

        Assert.Equal(groups.Sum(group => group.Value.Length), actual.Length);
        foreach (var group in groups)
        {
            foreach (var controllerName in group.Value)
            {
                var controller = Assert.Single(actual, type => type.Name == controllerName);
                Assert.Equal("Tongyin.Api.Controllers." + group.Key, controller.Namespace);
            }
        }
    }

    /// <summary>文件目录必须与命名空间相同，否则即使命名空间改对，VS 仍会显示杂乱文件。</summary>
    [Fact]
    public void Controllers_PhysicalFiles_AreSeparatedIntoMatchingFolders()
    {
        var root = FindControllersDirectory();
        Assert.Empty(Directory.GetFiles(root, "*Controller.cs", SearchOption.TopDirectoryOnly));

        var groups = new Dictionary<string, string[]>
        {
            ["Miniapp"] =
            [
                "MiniappConfig", "OrderTracking", "Orders", "ShippingAddress",
                "WechatAuth", "WechatPayment", "WechatProfile", "WorkOrder", "Works"
            ],
            ["Admin"] = ["AdminAccount", "AdminAiSettings", "AdminAiTasks", "AdminOrders", "AdminWorks", "WechatSystemSettings"],
            ["Internal"] = ["WechatPayNotify"],
            ["Shared"] = ["Media"]
        };

        foreach (var group in groups)
        {
            foreach (var controllerName in group.Value)
            {
                Assert.True(File.Exists(Path.Combine(root, group.Key, controllerName + "Controller.cs")),
                    $"{controllerName}Controller.cs 必须位于 Controllers/{group.Key}/ 下");
            }
        }
    }

    /// <summary>重构后保留小程序、后台、支付回调的 HTTP 地址，不破坏现有调用方。</summary>
    [Fact]
    public void ControllerBaseRoutes_RemainCompatible()
    {
        var routes = new Dictionary<string, string>
        {
            ["AdminAccountController"] = "api/admin/account",
            ["AdminAiSettingsController"] = "api/admin/ai/settings",
            ["AdminAiTasksController"] = "api/admin/ai",
            ["AdminOrdersController"] = "api/admin/orders",
            ["AdminWorksController"] = "api/admin/works",
            ["WechatSystemSettingsController"] = "api/admin/system/wechat",
            ["WechatPayNotifyController"] = "api/pay/notify",
            ["MiniappConfigController"] = "api/config",
            ["OrderTrackingController"] = "api/orders",
            ["OrdersController"] = "api/orders",
            ["ShippingAddressController"] = "api/shipping-address",
            ["WechatAuthController"] = "api/auth",
            ["WechatPaymentController"] = "api/orders",
            ["WechatProfileController"] = "api/me",
            ["WorkOrderController"] = "api/works",
            ["WorksController"] = "api/works"
        };

        var types = typeof(WorkResponse).Assembly.GetTypes();
        foreach (var route in routes)
        {
            var controller = Assert.Single(types, type => type.Name == route.Key);
            var attribute = controller.GetCustomAttribute<RouteAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(route.Value, attribute.Template);
        }

        var media = Assert.Single(types, type => type.Name == "MediaController");
        var get = media.GetMethod("Get");
        Assert.NotNull(get);
        Assert.Equal("/media/{file}", get.GetCustomAttribute<HttpGetAttribute>()?.Template);
    }

    private static string FindControllersDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var repoRoot = Path.Combine(directory.FullName, "backend-dotnet", "src", "Tongyin.Api", "Controllers");
            if (Directory.Exists(repoRoot))
            {
                return repoRoot;
            }

            var solutionRoot = Path.Combine(directory.FullName, "src", "Tongyin.Api", "Controllers");
            if (Directory.Exists(solutionRoot))
            {
                return solutionRoot;
            }
        }

        throw new DirectoryNotFoundException("无法找到童印后端 Controller 源码目录");
    }
}
