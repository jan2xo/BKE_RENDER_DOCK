#!/usr/bin/env python3
"""Disposable verifier contract test. The sqlite row is a FIXTURE, not an Agent grant."""
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile

def main():
    if not sys.platform.startswith("win"):
        print("SKIP: native Windows exact-process grant fixture")
        return
    verifier = Path(__file__).with_name("verify-installed-agent-startup-grant.py")
    with tempfile.TemporaryDirectory(prefix="bke-renderdock-audit-fixture-") as temp:
        db = Path(temp) / "fixture-grants.db"
        conn = sqlite3.connect(db)
        conn.execute("""
            CREATE TABLE startup_grants (
              pid INTEGER, started_utc_ticks INTEGER, user_sid TEXT,
              product_id TEXT, version TEXT, installation_id TEXT,
              image_sha256 TEXT, credential TEXT, granted_at TEXT,
              PRIMARY KEY(pid,started_utc_ticks,user_sid)
            )
        """)
        conn.execute("""
            INSERT INTO startup_grants VALUES (?,?,?,?,?,?,?,?,?)
        """, (31415, 638986123456789012, "S-1-5-21-111-222-333-1001",
              "bke-render-dock", "1.0.3",
              "00000000-0000-0000-0000-000000000001",
              "a"*64, "LICENSE_KEY", "2026-11-13T00:00:00.000Z"))
        conn.commit()
        conn.close()
        base = [
            sys.executable, str(verifier), "--db", str(db),
            "--pid", "31415", "--started-ticks", "638986123456789012",
            "--sid", "S-1-5-21-111-222-333-1001",
            "--sha256", "a"*64, "--product", "bke-render-dock",
            "--version", "1.0.3",
            "--installation-id", "00000000-0000-0000-0000-000000000001",
            "--signed-activation-mode", "LICENSE_KEY",
            "--expected-credential", "LICENSE_KEY"
        ]
        def run(args, should_pass, name):
            result = subprocess.run(args, capture_output=True, text=True,
                                    timeout=5)
            if should_pass:
                if result.returncode or json.loads(result.stdout)["status"] != "PASS":
                    raise RuntimeError("FAIL "+name+": "+result.stderr)
            elif result.returncode == 0:
                raise RuntimeError("FAIL "+name+": verifier incorrectly accepted mismatch")
            print("PASS fixture: "+name)
        def replace(option, value, args=base):
            copy = list(args)
            i = copy.index(option)
            copy[i+1] = value
            return copy
        run(base, True, "exact process/credential/image audit row")
        run(replace("--pid","31416"), False, "different PID")
        run(replace("--started-ticks","638986123456789013"), False, "recycled PID/start identity")
        run(replace("--sid","S-1-5-21-999-222-333-1001"), False, "different Windows user SID")
        run(replace("--sha256","b"*64), False, "different installed EXE hash")
        run(replace("--product","other-product"), False, "different product")
        run(replace("--version","1.0.4"), False, "different version")
        run(replace("--installation-id",
                    "00000000-0000-0000-0000-000000000002"),
            False, "different signed-policy installation ID")
        run(replace("--expected-credential","ACCOUNT"), False,
            "key grant cannot claim account credential")
        run(replace("--signed-activation-mode","ACCOUNT"), False,
            "signed ACCOUNT mode cannot authorize key grant")
        run(replace("--signed-activation-mode","EITHER"), True,
            "only signed EITHER can authorize an independently proved key grant")
        run(replace("--expected-credential","EITHER"), False,
            "EITHER is not a credential")
        run(replace("--signed-activation-mode","AUTO"), False,
            "unsigned or unknown activation mode is denied")
        run(replace("--db",str(Path(temp)/"missing.db")), False,
            "no durable Agent ledger")
        # A real durable row with the right PID/SID/image but the wrong
        # installation must NEVER be counted as the signed installation.
        connection = sqlite3.connect(db)
        try:
            connection.execute(
                "UPDATE startup_grants SET installation_id = ?",
                ("00000000-0000-0000-0000-000000000002",))
            connection.commit()
        finally:
            connection.close()
        run(base, False, "wrong installation ID inside protected grant row")

        # The same protected process row may carry an actual ACCOUNT grant.
        # Credential identity and canonical DS mode must be checked separately.
        connection = sqlite3.connect(db)
        try:
            connection.execute(
                "UPDATE startup_grants SET installation_id = ?, credential = ?",
                ("00000000-0000-0000-0000-000000000001", "ACCOUNT"))
            connection.commit()
        finally:
            connection.close()
        account = replace("--expected-credential", "ACCOUNT")
        run(replace("--signed-activation-mode","ACCOUNT",account), True,
            "signed ACCOUNT accepts independent account credential")
        run(replace("--signed-activation-mode","EITHER",account), True,
            "signed EITHER accepts independent account credential")
        run(account, False, "signed LICENSE_KEY denies account credential")
        run(replace("--signed-activation-mode","EITHER"), False,
            "EITHER policy does not permit claiming a key credential from account")
        run(base, False, "account grant cannot be mislabeled key credential")
        print("Read-only installed-Agent audit verifier disposable fixtures: 20/20 PASS")

if __name__ == "__main__":
    main()
