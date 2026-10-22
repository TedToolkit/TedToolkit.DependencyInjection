// -----------------------------------------------------------------------
// <copyright file="Names.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection.Analyzer;

internal static class Names
{
    internal const string REGISTRATION_ATTRIBUTE = "TedToolkit.DependencyInjection.ServiceRegistrationAttribute";

    internal const string REMOVE_ATTRIBUTE = "TedToolkit.DependencyInjection.RemoveAllAttribute";

    internal const string MODULE_ATTRIBUTE = "TedToolkit.DependencyInjection.GeneratedServiceModuleAttribute";

    internal const string CONFIGURATION_ATTRIBUTE = "TedToolkit.DependencyInjection.ModuleConfigurationAttribute";

    internal const string COLLECTION = "Microsoft.Extensions.DependencyInjection.IServiceCollection";

    internal const string PROVIDER = "System.IServiceProvider";

    internal const string GENERATED_NAMESPACE = "Microsoft.Extensions.DependencyInjection";
}
