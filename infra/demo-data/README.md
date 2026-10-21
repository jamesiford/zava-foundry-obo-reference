# Demo data (`infra/demo-data/`)

Short, realistic Zava Credit Union documents with concrete numbers and rules, so every demo answer is checkable. They are
the source for the three SharePoint libraries that `scripts/Seed-SharePointTestData.ps1` creates. The script converts each
Markdown file to a real `.docx` in memory, because the Copilot Retrieval API only does semantic/hybrid retrieval for
`.docx`, `.pptx`, `.pdf`, `.aspx` and `.one`; other extensions (such as `.md` or `.txt`) get lexical matching only.

| Library | Folder | Who can read it |
|---|---|---|
| Lending Policy | `lending-policy/` | **Daniel Park** (User A, Credit Officer) only |
| Portfolio and Collections | `portfolio-and-collections/` | **Maya Lin** (User B, Portfolio Analyst - West) only |
| Member Services FAQ | `member-services-faq/` | Both |

Permissions are enforced by SharePoint (unique library permissions, nothing inherited from the site), not by this repo. The
isolation story is the point: the same question asked by two people must return different results, and the bot must never
need to know why.

> SharePoint needs time to crawl and index new files before the Retrieval API can return them. Allow roughly 15 minutes
> (occasionally up to an hour) after seeding before the first demo question; ask a sentinel question as each person first.
> In the first live run the documents were answerable about 30 minutes after they were uploaded.
>
> **Check the libraries in the browser after seeding.** The seed script once reported *Skipped (exists)* for files that were never
> uploaded (a script bug, fixed on 21 Oct 2026). Still check: if a library is empty, upload the `.docx` files by hand into the library of the same name; they can be generated
> without PnP by dot-sourcing `scripts/Compass.DemoDocuments.ps1` and calling `ConvertTo-DocxByteArray` on each Markdown file.
> Install `PnP.PowerShell` for all users or outside OneDrive: its help files in a OneDrive module path trigger Purview DLP alerts.

## Demo questions and expected answers

"Gets it" means the answer is returned and cites that document. "Must not" means the person must get no result from that
document (the correct outcome is the briefing saying "No document you have access to answers this question." for a policy question, never an error and never the other person's text).

### Consumer Lending Policy (`lending-policy/Consumer Lending Policy.md`)

| # | Ask | Expected answer | Daniel (A) | Maya (B) |
|---|---|---|---|---|
| 1 | "What is the maximum debt-to-income ratio for an unsecured personal loan?" | 43 percent, calculated including the new loan payment | Gets it | **Must not** |
| 2 | "Does an unsecured loan over $25,000 need a second approval?" | Yes - sign-off from a second underwriter before funding | Gets it | **Must not** |
| 3 | "What is the maximum loan-to-value for a used auto loan?" | 90 percent of retail book value (max term 60 months, minimum score 660) | Gets it | **Must not** |

### Credit Officer Lending Authority (`lending-policy/Credit Officer Lending Authority.md`)

| # | Ask | Expected answer | Daniel (A) | Maya (B) |
|---|---|---|---|---|
| 4 | "How much unsecured credit can a Credit Officer approve on their own?" | Up to $25,000 (a Senior Credit Officer: up to $35,000) | Gets it | **Must not** |
| 5 | "Who decides loans above a Senior Credit Officer's limit?" | The Credit Committee, which meets every Tuesday and decides within two business days | Gets it | **Must not** |

### Collections Playbook (`portfolio-and-collections/Collections Playbook.md`)

| # | Ask | Expected answer | Daniel (A) | Maya (B) |
|---|---|---|---|---|
| 6 | "When must we make a hardship call on a delinquent account?" | At 60 days past due, before any additional fee is assessed | **Must not** | Gets it |
| 7 | "What is the late fee and can it be waived?" | $25 flat; waived once per member every 12 months | **Must not** | Gets it |
| 8 | "What is the minimum payment on a hardship plan?" | At least 50 percent of the regular monthly payment, for up to 12 months | **Must not** | Gets it |

### West Region Portfolio Review Q3 (`portfolio-and-collections/West Region Portfolio Review Q3.md`)

| # | Ask | Expected answer | Daniel (A) | Maya (B) |
|---|---|---|---|---|
| 9 | "Which branches are on the delinquency watchlist this quarter?" | Fresno Central (1.9 percent) and Reno South (1.7 percent), both above the 1.5 percent trigger | **Must not** | Gets it |
| 10 | "What was the West region 30-plus day delinquency rate in Q3?" | 2.9 percent, up from 2.4 percent in Q2 | **Must not** | Gets it |

### Shared control question (`member-services-faq/Member Services FAQ.md`)

| # | Ask | Expected answer | Daniel (A) | Maya (B) |
|---|---|---|---|---|
| 11 | "What is the cutoff time for outgoing domestic wires?" | 3:30 PM Eastern on business days; later requests go out the next business day | Gets it | Gets it |

Question 11 is the control: if it fails for either person the problem is indexing, licensing or configuration, not
isolation. Run it first.

## Member-account demo (MCP, not SharePoint)

The seeded Zava API (`ZavaApiAgent` -> MCP) maps the two people to member records by Entra object id (no ids live in source;
`scripts/Set-MemberMapping.ps1` resolves them at run time):

| Person | Member number | Member record |
|---|---|---|
| Daniel Park (User A) | `ZCU-100234` | Active, seeded in the MCP server |
| Maya Lin (User B) | `ZCU-100871` | Active, seeded in the MCP server |

Asking "give me a briefing on my account" as each person returns their own member data and never the other's.
