# Negative Constraints

Do NOT:

1. Rename the command from endo.
2. Use dev as the CLI command.
3. Assume arbitrary global installations.
4. Install everything globally.
5. Make specialized tools universally available.
6. Treat availability as dependency.
7. Create an unnecessarily complicated capability abstraction.
8. Blindly install discovered GitHub repositories.
9. Prefer stale releases when active source is available.
10. Skip reading a repository README.
11. Declare a build failed after the first error.
12. Register a tool before validation.
13. Delete Scratchpad evidence immediately.
14. Overwrite old validated versions.
15. Assume every update is better.
16. Force projects onto new tool versions.
17. Treat latest as blind upstream HEAD.
18. Treat live as automatically trusted.
19. Silently remove tools required by projects.
20. Require --force for ordinary operation.
21. Replace project .git with DevRepo.
22. Store every project binary/source file in DevRepo.
23. Turn DevRepo into a complete machine backup.
24. Make restore destructive by default.
25. Delete unknown existing state during restore.
26. Silently infer critical setup choices.
27. Add unnecessary approval gates to ordinary requested actions.
28. Make AI an independent hidden implementation.
29. Hard-code one AI provider.
30. Merge project .agents/ with Endo AI.
31. Require an IDE to open a project.
32. Put all workflow narrative into JSON.
33. Artificially minimize environment.json.
34. Turn environment.json into an opaque database dump.
35. Sacrifice state integrity for convenience.
36. Permit infinite AI retry loops.
37. Claim success without actual evidence.
38. Delete denied tasks.
39. Treat denial as abandonment.
40. Overengineer capabilities without a demonstrated need.
41. Freeze implementation details that do not need to be frozen.

The architecture is stable.

Implementation details may evolve as long as they preserve the architectural intent.
