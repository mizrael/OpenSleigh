---
name: triage-pr
description: Fetch, analyze, and triage PR review comments — present report for user approval before any action
disable-model-invocation: true
argument-hint: "[pr-number]"
---

# PR Review Comment Triage

Triage all review comments on PR #$ARGUMENTS in the current repository.

**CRITICAL: Do NOT reply to any comments, modify any code, or take any action until the user explicitly approves. This workflow is report-first, act-later.**

## Step 1: Fetch comments

Use `gh api` to fetch:
- Pull request review comments: `gh api repos/{owner}/{repo}/pulls/{pr}/comments`
- General issue comments: `gh api repos/{owner}/{repo}/issues/{pr}/comments`
- Reviews summary: `gh pr view {pr} --json reviews`

Extract the owner/repo from the current git remote.

## Step 2: Analyze each comment

For every review comment, read the actual source code referenced by the comment before judging. Do not rely solely on the diff hunk. Classify each comment as:

- **Valid — Should Fix**: A genuine bug, correctness issue, missing state update, or meaningful improvement.
- **Valid — Style/Nice-to-have**: Correct observation but low priority (naming, minor refactors).
- **False Positive**: The reviewer misunderstood the code, the concern doesn't apply in context, or the suggestion is strictly worse. Common false positives include:
  - Coordinate system swaps that are intentional
  - "Implicit filtering" style nits when `if/continue` is idiomatic
  - Integer overflow warnings on small UI values
  - Suggestions that introduce double work (e.g., calling TryParse twice)
  - Unreachable edge cases due to known data constraints

## Step 3: Present the report

Present a full report to the user with these sections. Skip bot comments (Cloudflare deploy notifications, CI status, etc.).

### Valid Issues — Should Fix
| # | File:Line | Issue | Recommended Fix |
|---|-----------|-------|-----------------|

### Valid Issues — Style/Nice-to-have
| # | File:Line | Issue | Recommendation |
|---|-----------|-------|----------------|

### False Positives
| # | File:Line | Reviewer Says | Why It's Wrong |
|---|-----------|--------------|----------------|

## Step 4: Wait for user approval

After presenting the report, ask the user what they want to do. Offer these options:
1. **Fix valid issues + reply to false positives** — apply code fixes, reply to false positives on GitHub, commit and push
2. **Fix valid issues only** — apply code fixes, commit and push, don't reply on GitHub
3. **Reply to false positives only** — post replies on GitHub, don't change code
4. **Do nothing** — the report was informational only

For replying to false positives, use:
```
gh api repos/{owner}/{repo}/pulls/{pr}/comments/{comment_id}/replies -X POST -f body="..."
```
Group similar false positives — reply to the first in a cluster and note it covers the others.

For code fixes:
1. Apply the fixes
2. Run `dotnet build` to verify
3. Commit with a message referencing the PR review
4. Push to the branch

Do NOT merge or close the PR.
