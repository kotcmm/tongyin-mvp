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

    def test_api_starts_empty_without_business_logic(self):
        """本阶段的 HTTP 宿主不含任何业务入口或业务实现。"""
        api = ROOT / "src/Tongyin.Api"
        self.assertTrue((api / "Program.cs").is_file())
        self.assertEqual([p.name for p in api.rglob("*.cs")], ["Program.cs"])
        text = (api / "Program.cs").read_text(encoding="utf-8")
        self.assertNotIn("MapGet", text)
        self.assertNotIn("MapPost", text)
        self.assertNotIn("MapControllers", text)


if __name__ == "__main__":
    unittest.main()
