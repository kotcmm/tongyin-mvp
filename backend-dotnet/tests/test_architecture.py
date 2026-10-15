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

    def test_api_only_exposes_current_work_creation_boundary(self):
        """API 只能装配当前已经迁移的作品创建入口。"""
        api = ROOT / "src/Tongyin.Api"
        self.assertTrue((api / "Program.cs").is_file())
        sources = {p.name for p in api.rglob("*.cs") if not set(p.parts).intersection({"obj", "bin"})}
        self.assertEqual({"Program.cs", "WorkEndpoints.cs"}, sources)
        source = (api / "WorkEndpoints.cs").read_text(encoding="utf-8")
        self.assertIn('MapPost("/api/works"', source)
        self.assertNotIn("MapControllers", source)
        self.assertNotIn('MapPost("/api/works/{id}/generate"', source)


if __name__ == "__main__":
    unittest.main()
