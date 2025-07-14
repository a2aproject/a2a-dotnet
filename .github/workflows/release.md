# Release Process

The following process is used when publishing new releases to NuGet.org:

1. **Update the source to increment the version number**
    - Before publishing a new release, the [`Directory.Build.props`](../../Directory.Build.props) file needs to be updated to bump the version to the next expected release version

2. **Create a new Release in GitHub**
    - Use the link on the repo home page to [Create a new release](https://github.com/a2aproject/a2a-dotnet/releases/new)
    - Click the 'Choose a tag' dropdown button
        - Type the name using the `v{major}.{minor}.{patch}-{suffix}` pattern
        - Click 'Create new tag: ... on publish'
    - Click the 'Target' dropdown button
        - Choose the 'main' branch
    - Click the 'Generate release notes button'
        - This will add release notes into the Release description
        - The generated release notes include what has changed and the list of new contributors
    - Verify the Release title
        - It will be populated to match the tag name to be created
        - This should be retained, using the release title format matching the `v{major}.{minor}.{patch}-{suffix}` format
    - Augment the Release description as desired
        - This content is presented used on GitHub and is not persisted into any artifacts
    - Check the 'Set as a pre-release' button under the release description if appropriate
    - Click 'Publish release'

3. **Monitor the Release workflow**
    - After publishing the release, a workflow will begin for producing the release's build artifacts and publishing the NuGet packages to NuGet.org
    - If the job fails, troubleshoot and re-run the workflow as needed
    - Verify the package versions become listed on NuGet.org:
        - [A2A](https://www.nuget.org/packages/A2A)
        - [A2A.AspNetCore](https://www.nuget.org/packages/A2A.AspNetCore)

