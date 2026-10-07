# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Additional labels

| Label         | Meaning                                            |
| ------------- | -------------------------------------------------- |
| `qa-verified` | Issue has been verified by QA / human testing      |

## Blocked issues

Blocking is a native GitHub issue dependency, not a label. A blocked issue shows an inline "Blocked" chip in the issue list and a "Blocked by" section in its sidebar.

```bash
# Mark <issue> blocked by <blocker-number>
gh api repos/{owner}/{repo}/issues/<issue>/dependencies/blocked_by \
  -X POST -F issue_id=$(gh api repos/{owner}/{repo}/issues/<blocker-number> --jq .id)

# List an issue's blockers
gh api repos/{owner}/{repo}/issues/<issue>/dependencies/blocked_by
```

Note: `issue_id` in the POST body is the issue's database ID, not its number — fetch it first as shown. Keep the body's `## Blocked by` section in sync; it records the history (which blockers landed, which are still open) that the chip doesn't show.
