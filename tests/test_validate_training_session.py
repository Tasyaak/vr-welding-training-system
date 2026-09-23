import hashlib, json, tempfile, unittest
from pathlib import Path
from tools.validate_training_session import validate

class SessionValidatorTests(unittest.TestCase):
    def build(self):
        temp=tempfile.TemporaryDirectory(); root=Path(temp.name); attempt=root/"attempts"/"a"; attempt.mkdir(parents=True)
        (root/"session.json").write_text(json.dumps({"schemaVersion":1,"completed":True}),encoding="utf-8")
        data=b'{"kind":"sample"}\n'; (attempt/"records-0000.jsonl").write_bytes(data)
        (attempt/"summary.json").write_text(json.dumps({"complete":True}),encoding="utf-8")
        (attempt/"manifest.json").write_text(json.dumps({"complete":True,"chunks":[{"file":"records-0000.jsonl","bytes":len(data),"sha256":hashlib.sha256(data).hexdigest()}]}),encoding="utf-8")
        return temp,root
    def test_complete_session_passes(self):
        temp,root=self.build()
        with temp: self.assertTrue(validate(root)["ok"])
    def test_tampered_chunk_fails(self):
        temp,root=self.build()
        with temp:
            (root/"attempts"/"a"/"records-0000.jsonl").write_bytes(b"tampered")
            self.assertFalse(validate(root)["ok"])
    def test_partial_session_fails(self):
        temp,root=self.build()
        with temp:
            (root/"orphan.partial").write_text("x",encoding="utf-8")
            self.assertFalse(validate(root)["ok"])
if __name__ == "__main__": unittest.main()
