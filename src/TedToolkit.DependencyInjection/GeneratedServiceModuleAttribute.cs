// -----------------------------------------------------------------------
// <copyright file="GeneratedServiceModuleAttribute.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace TedToolkit.DependencyInjection;

/// <summary>
/// Exports a compile-time module entry point for generators; runtime registration does not use reflection.
/// </summary>
/// <param name="typeName">The fully qualified metadata name of the generated entry point type.</param>
/// <param name="methodName">The name of the public static module method.</param>
/// <remarks>
/// The generator emits this assembly-level metadata. Generators in referencing projects read it
/// to call the exported registration method directly, even when project and assembly names differ.
/// Application code should not declare this attribute manually.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GeneratedServiceModuleAttribute(string typeName, string methodName) : Attribute
{
    /// <summary>
    /// Gets the fully qualified metadata name of the generated entry point type.
    /// </summary>
    public string TypeName { get; } = typeName;

    /// <summary>
    /// Gets the name of the public static module method.
    /// </summary>
    public string MethodName { get; } = methodName;
}
