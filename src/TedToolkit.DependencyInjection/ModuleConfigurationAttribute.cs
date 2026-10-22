// -----------------------------------------------------------------------
// <copyright file="ModuleConfigurationAttribute.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection;

/// <summary>
/// Represents a method that applies explicit MSDI configuration to the current module's service collection.
/// </summary>
/// <remarks>
/// The method must be accessible, static, non-generic, return void, and accept exactly one IServiceCollection parameter.
/// Methods run in fully qualified name order after local registrations, dependency modules, removals, and replacements,
/// before the partial ConfigureServices hook. They run on every module call. Exceptions propagate without rolling back
/// earlier registrations. Use these methods for runtime object keys, existing runtime instances, or arbitrary MSDI setup.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class ModuleConfigurationAttribute : Attribute;
