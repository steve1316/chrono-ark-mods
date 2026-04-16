1. When you make changes, commit them into small and concise commits.
2. The commit messages has no prefix, is imperative language, and has no extra bit at the end saying that it was co-authored.
3. The commit messages only consist of the first line of the commit message. No extra sentences on a newline / paragraph.
4. Comments and docstrings end in periods for proper grammar and punctuation.
5. Comments should explain *why*, not *what*. Focus on non-obvious reasoning: callback ordering, guard conditions, why something is done a specific way. Don't comment self-evident code.
6. Keep comments short (1-2 lines). All methods need a `/// <summary>` block.
7. Decompiled game source (Assembly-CSharp.dll) is in `decompiled/` (gitignored). Grep it directly instead of running ilspycmd repeatedly.