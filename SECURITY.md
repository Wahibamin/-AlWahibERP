# Security & data handling

AlWahib ERP is a local **prototype**, not an audited finance product.

- Never submit real company databases (`*.db`, `*.sqlite*`) or backups to public issues or pull requests.
- Do not embed passwords, API keys, or company financial information in screenshots or code.
- The database is stored on the local Windows machine; database-at-rest encryption and comprehensive file access hardening are **not** included.
- Role checks and audit hash chaining are implemented in parts of the code but have **not** undergone a formal security audit.
- For a security concern, avoid publishing exploit details or sensitive data in a public issue. Contact the repository owner using a private channel they publish in their GitHub profile.

See `docs/ENGINEERING_NOTES.md` and `docs/WINDOWS_ACCEPTANCE_CHECKLIST.md` for known security and deployment gaps.
