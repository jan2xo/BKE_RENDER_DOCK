#!/usr/bin/env python3
"""Read-only exact-process grant checker for approved disposable Windows machines.

Never mints credentials, updates a database, creates SQL authority or changes
permissions. No user SID, private key, token or whole database is printed.
"""
import argparse
import hashlib
import json
import os
import re
import sqlite3
import sys
from pathlib import Path

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--db", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--started-ticks", required=True, type=int)
    parser.add_argument("--sid", required=True)
    parser.add_argument("--sha256", required=True)
    parser.add_argument("--product", required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--installation-id", required=True)
    parser.add_argument("--signed-activation-mode",
                        choices=["ACCOUNT", "LICENSE_KEY", "EITHER"],
                        required=True)
    # EITHER is a signed DS activation mode, never an actual credential.
    parser.add_argument("--expected-credential",
                        choices=["ACCOUNT", "LICENSE_KEY"],
                        required=True)
    args = parser.parse_args()
    if (not sys.platform.startswith("win") or args.pid < 1 or
            args.started_ticks < 1 or len(args.sid) < 6 or
            not args.sid.startswith("S-1-") or
            len(args.sha256) != 64 or
            any(c not in "0123456789abcdef" for c in args.sha256) or
            not args.product or not args.version or
            not re.fullmatch(r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}",
                             args.installation_id)):
        raise RuntimeError("Refusing invalid or non-Windows startup grant proof")
    db_path = Path(args.db)
    if not db_path.is_file():
        raise RuntimeError("Agent durable grant database missing")
    connection = sqlite3.connect(db_path.as_uri() + "?mode=ro", uri=True,
                                 timeout=3)
    try:
        connection.execute("PRAGMA query_only=ON")
        rows = connection.execute("""
            SELECT pid, started_utc_ticks, user_sid, product_id, version,
                   installation_id, image_sha256, credential, granted_at
              FROM startup_grants
             WHERE pid = ? AND started_utc_ticks = ? AND user_sid = ?
        """, (args.pid, args.started_ticks, args.sid)).fetchall()
    finally:
        connection.close()
    if len(rows) != 1:
        raise RuntimeError("No unique durable exact-process Agent grant")
    pid, ticks, sid, product, version, installation, digest, credential, granted = rows[0]
    if (product != args.product or version != args.version or
            digest != args.sha256 or credential not in ("ACCOUNT", "LICENSE_KEY") or
            credential != args.expected_credential or
            (args.signed_activation_mode != "EITHER" and
             credential != args.signed_activation_mode) or
            not isinstance(installation, str) or
            installation != args.installation_id):
        raise RuntimeError(
            "Agent grant mismatches signed installation, activation mode, "
            "binary, product or independently selected credential")
    # Never print raw SID, machine ID, installation ID, or user profile paths.
    print(json.dumps({
        "status": "PASS", "product": product, "version": version,
        "credential": credential, "image_sha256": digest,
        "process_id": pid, "started_utc_ticks": ticks,
        "sid_sha256": hashlib.sha256(sid.encode("ascii")).hexdigest(),
        "grant_utc": granted,
        "durable_ledger": "verified_exact_process"
    }, separators=(",", ":")))

if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, sqlite3.Error, OSError) as error:
        print("FAIL: " + str(error), file=sys.stderr)
        sys.exit(1)
