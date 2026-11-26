"""Partes do ensaio local de release que nao dependem da API (#398)."""
import base64
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import release_rehearsal as rehearsal  # noqa: E402

# RFC 6238, apendice B: segredo publico de teste ASCII "12345678901234567890" (SHA-1), ultimos 6 digitos.
# Derivado em tempo de execucao para nao fixar no codigo uma string de alta entropia.
RFC6238_SECRET = base64.b32encode(b"12345678901234567890").decode("ascii")


class DocumentDigitsTests(unittest.TestCase):
    def test_cpf_digits(self):
        self.assertEqual(rehearsal.cpf(111444777), "11144477735")

    def test_cnpj_digits(self):
        self.assertEqual(rehearsal.cnpj(11222333), "11222333000181")


class TotpTests(unittest.TestCase):
    def test_rfc6238_vectors(self):
        for at, expected in ((59, "287082"), (1111111109, "081804"), (1234567890, "005924"), (2000000000, "279037")):
            with self.subTest(at=at):
                self.assertEqual(rehearsal.totp(RFC6238_SECRET, at), expected)

    def test_accepts_secret_without_padding_and_lowercase(self):
        self.assertEqual(rehearsal.totp(RFC6238_SECRET.lower(), 59), "287082")


class SafetyGuardTests(unittest.TestCase):
    def test_only_local_api(self):
        for url in ("http://localhost:5002", "http://127.0.0.1:5001/", "http://[::1]:5000"):
            with self.subTest(url=url):
                self.assertEqual(rehearsal.require_local(url), url.rstrip("/"))
        for url in ("https://hemodinks-api-1-90nb.onrender.com", "https://api.hemodinks.gestao-saude.tec.br",
                    "http://localhost.example.com"):
            with self.subTest(url=url), self.assertRaises(rehearsal.RehearsalError):
                rehearsal.require_local(url)

    def test_state_file_must_stay_outside_repository(self):
        for path in (rehearsal.ROOT / "estado.json", rehearsal.ROOT / "scripts" / "estado.json", rehearsal.ROOT):
            with self.subTest(path=path), self.assertRaises(rehearsal.RehearsalError):
                rehearsal.require_outside_repo(path)
        with tempfile.TemporaryDirectory() as tmp:
            outside = Path(tmp) / "estado.json"
            self.assertEqual(rehearsal.require_outside_repo(outside), outside.resolve())


class ItemsTests(unittest.TestCase):
    def test_reads_list_or_paged_payload(self):
        self.assertEqual(rehearsal.items([{"id": 1}]), [{"id": 1}])
        self.assertEqual(rehearsal.items({"items": [{"id": 2}], "total": 1}), [{"id": 2}])
        self.assertEqual(rehearsal.items({"message": "erro"}), [])
        self.assertEqual(rehearsal.items(None), [])


if __name__ == "__main__":
    unittest.main()
