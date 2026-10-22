// -----------------------------------------------------------------------
// <copyright file="ServiceModel.cs" company="TedToolkit contributors">
// Copyright (c) TedToolkit contributors. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.CodeAnalysis;

namespace TedToolkit.DependencyInjection.Analyzer;

internal sealed class ServiceModel(
    ISymbol declaration,
    ITypeSymbol implementation,
    ITypeSymbol contract,
    string lifetime,
    string mode,
    TypedConstant? key,
    IMethodSymbol? constructor = null)
{
    internal ISymbol Declaration { get; } = declaration;

    internal ITypeSymbol Implementation { get; } = implementation;

    internal ITypeSymbol Contract { get; } = contract;

    internal IMethodSymbol? Constructor { get; } = constructor;

    internal string Lifetime { get; } = lifetime;

    internal string Mode { get; } = mode;

    internal TypedConstant? Key { get; } = key;
}
