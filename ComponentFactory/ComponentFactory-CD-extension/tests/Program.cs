using FactoryChecks;

await TemplateCustomizationChecks.RunAsync();
await GitLabClientChecks.RunAsync();
await GitRepositoryChecks.RunAsync();
await GenerationChecks.RunAsync();
await CdProvisioningChecks.RunAsync();
NameAndConfigurationChecks.Run();

Console.WriteLine($"All {Assert.PassedCount} checks passed.");
