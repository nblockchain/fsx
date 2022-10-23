It is assumed that by contributing to this repository (or any repository
under the [nblockchain github org](https://github.com/nblockchain/)) in the
form of PullRequests, you grant the intellectual property of your
contribution under the terms of the MIT licence. If you don't wish to
comply with this policy, you can keep a fork in your github account.


# F# Coding Style

* For formatting/indentation, please use fantomless-tool (install it
via `dotnet tool install -g fantomless-tool --version $version`, using
the same $version we use, which you can find in our CI pipelines). In
fact our CI checks that the formatting is the same as the one done by
this tool, so we recommend you to install this in a git pre-commit hook;
see how to do this here:
https://github.com/nblockchain/fantomless/commit/138146e3e8fc7e8d9d8404ef9956ace3f529c127
* For the rest of style not covered by fantomas, please read our
[F# Coding Style](https://github.com/nblockchain/conventions/blob/master/FSharpStyleGuide.md).


# Workflow guidelines

For reference on how we like commit messages to be formatted, and other
recommendations, please read our
[Workflow guidelines](https://github.com/nblockchain/conventions/blob/master/WorkflowGuidelines.md).
