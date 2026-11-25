import importlib.util
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).parents[2]
spec = importlib.util.spec_from_file_location("shards", ROOT / "scripts" / "ci_test_shards.py")
shards = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shards)


def matches(filter_text, fqn):
    """Avaliador minimo do filtro gerado: termos com & (parte 1) ou | entre parenteses (parte 2)."""
    if filter_text.startswith("("):
        return any(term.split("~", 1)[1] in fqn for term in filter_text[1:-1].split("|"))
    for term in filter_text.split("&"):
        if term.startswith("FullyQualifiedName!~") and term.split("~", 1)[1] in fqn:
            return False
    return True


class ShardFilterTests(unittest.TestCase):
    def test_every_test_runs_in_exactly_one_shard(self):
        classes = shards.shard_two_classes()
        one, two = shards.test_filter(1, classes), shards.test_filter(2, classes)
        samples = [f"{name}.Metodo(x: 1)" for name in classes] + [
            "HemodinksAPI.Tests.ClasseNova.Teste",
            "HemodinksAPI.Tests.SessionRenewalEndpointTestsExtra.Teste",  # prefixo de uma classe listada
            "HemodinksAPI.Tests.PacienteCommandHandlerTests.InativarPaciente_SqlServer_PreservaDadosERespeitaTenant",
        ]
        for fqn in samples:
            self.assertEqual(1, matches(one, fqn) + matches(two, fqn), fqn)
        self.assertFalse(matches(one, "HemodinksAPI.Tests.LoginBrowserTests.Browser_UsesRealApi"))
        self.assertFalse(matches(two, "HemodinksAPI.Tests.LoginBrowserTests.Browser_UsesRealApi"))

    def test_listed_classes_exist_in_the_test_project(self):
        sources = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "HemodinksAPI.Tests").rglob("*.cs")
                            if "/bin/" not in p.as_posix() and "/obj/" not in p.as_posix())
        for name in shards.shard_two_classes():
            short = name.rsplit(".", 1)[1]
            self.assertRegex(sources, rf"\bclass {re.escape(short)}\b", name)

    def test_invalid_lists_are_rejected(self):
        bad = ROOT / "scripts" / "tests" / "_shard_list_tmp.txt"
        try:
            for content in ("", "HemodinksAPI.Tests.LoginBrowserTests\n", "A.B\n", "HemodinksAPI.Tests.X\nHemodinksAPI.Tests.X\n"):
                bad.write_text(content, encoding="utf-8")
                with self.assertRaises(ValueError):
                    shards.shard_two_classes(bad)
        finally:
            bad.unlink(missing_ok=True)


if __name__ == "__main__":
    unittest.main()
