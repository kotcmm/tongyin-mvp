"""童印 .NET 最小分层骨架的结构验收，不涉及业务实现。"""

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAYERS = ("Domain", "Application", "Infrastructure", "Api")


class ArchitectureSkeletonTests(unittest.TestCase):
    def test_four_independent_projects_exist(self):
        """每一层拥有独立 csproj，并统一纳入解决方案。"""
        solution = (ROOT / "Tongyin.slnx").read_text(encoding="utf-8")
        for layer in LAYERS:
            relative = f"src/Tongyin.{layer}/Tongyin.{layer}.csproj"
            with self.subTest(layer=layer):
                self.assertTrue((ROOT / relative).is_file())
                self.assertIn(f'Project Path="{relative}"', solution)

    def test_dependencies_only_point_inward(self):
        """基础分层的引用方向固定，禁止 API 承载内层职责。"""
        expected = {
            "Domain": set(),
            "Application": {"Domain"},
            "Infrastructure": {"Application"},
            "Api": {"Application", "Infrastructure"},
        }
        for layer, should_reference in expected.items():
            with self.subTest(layer=layer):
                project = ROOT / f"src/Tongyin.{layer}/Tongyin.{layer}.csproj"
                refs = {
                    Path(node.attrib["Include"].replace("\\", "/")).stem.replace("Tongyin.", "")
                    for node in ET.parse(project).iter("ProjectReference")
                }
                self.assertEqual(refs, should_reference)

    def test_existing_http_routes_use_controllers(self):
        """现有作品及媒体接口统一采用 ASP.NET Core Controller 路由，禁止旧 Minimal API 并存。"""
        api = ROOT / "src/Tongyin.Api"
        sources = {p.name for p in api.rglob("*.cs") if not set(p.parts).intersection({"obj", "bin"})}
        self.assertEqual({
            "Program.cs",
            "WorksController.cs",
            "MediaController.cs",
            "WorkResponse.cs",
            "WechatSessionAuthenticationHandler.cs",
            "EnabledWechatUserAuthorizationHandler.cs",
            "EnabledWechatUserRequirement.cs",
            "AuthorizationPolicies.cs",
        }, sources)

        program = (api / "Program.cs").read_text(encoding="utf-8")
        self.assertIn("AddControllers()", program)
        self.assertIn("MapControllers()", program)
        self.assertNotIn("WorkEndpoints.Map", program)
        self.assertIn("UseAuthentication()", program)
        self.assertIn("UseAuthorization()", program)
        self.assertIn("AddPolicy(AuthorizationPolicies.Customer", program)

        works = (api / "Controllers" / "Customer" / "WorksController.cs").read_text(encoding="utf-8")
        self.assertIn("ControllerBase", works)
        self.assertIn("[ApiController]", works)
        self.assertIn('[Route("api/works")]', works)
        self.assertIn("[Authorize(Policy = AuthorizationPolicies.Customer)]", works)
        self.assertIn("[HttpPost]", works)
        self.assertIn("[HttpGet]", works)
        self.assertNotIn("HttpContext context", works)
        self.assertNotIn("TryValidate(", works)

        media = (api / "Controllers" / "MediaController.cs").read_text(encoding="utf-8")
        self.assertIn("ControllerBase", media)
        self.assertIn('[HttpGet("/media/{file}")]', media)
        self.assertNotIn("[Authorize", media)


if __name__ == "__main__":
    unittest.main()
