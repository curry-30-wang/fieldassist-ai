# FieldAssist local database

The default local database is the ignored SQLite file `database/fieldassist.db`.
Create its schema and deterministic demo records from the repository root with:

```powershell
python -m backend.seed
```

The seed can be run repeatedly. It identifies users by email, knowledge documents
by title, and evaluation cases by question, so existing demo records are not
duplicated or overwritten.

The two demo accounts are `admin@fieldassist.local` and
`employee@fieldassist.local`. Both use the password configured by
`DEMO_ADMIN_PASSWORD`; only salted password hashes are stored in the database.

Password hashes use this format:

```text
pbkdf2_sha256$600000$<base64url salt without padding>$<base64url digest without padding>
```

The salt is 16 random bytes. The digest is PBKDF2-HMAC-SHA256 over the UTF-8
password with 600,000 iterations and a 32-byte output.

Provider source metadata and evaluation keyword lists are stored as JSON text so
the schema remains compatible with SQLite.
