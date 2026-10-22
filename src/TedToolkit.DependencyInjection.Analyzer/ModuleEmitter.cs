using Microsoft.CodeAnalysis;

using TedToolkit.RoslynHelper;
using TedToolkit.RoslynHelper.Syntaxes;

using static TedToolkit.RoslynHelper.SourceComposer;
using static TedToolkit.RoslynHelper.SourceComposer<TedToolkit.DependencyInjection.Analyzer.ServiceModuleGenerator>;

namespace TedToolkit.DependencyInjection.Analyzer;

internal static class ModuleEmitter
{
    internal static void Emit(ModuleModel model, SourceProductionContext context)
    {
        var compilation = model.Compilation;
        var collection = DataType.FromSymbol(compilation.GetTypeByMetadataName(Names.COLLECTION)!, compilation);
        var type = Class(model.TypeName).Public.Static.Partial
            .AddRootDescription(new DescriptionSummary(new DescriptionText("Registers services for the current project and its referenced modules.")));
        var entry = Method(model.MethodName, new ReturnType(collection)).Public.Static
            .AddParameter(new Parameter(collection, "services").This)
            .AddRootDescription(new DescriptionSummary(new DescriptionText("Registers local services, dependency modules, removals, replacements, and configuration in that order.")))
            .AddStatement(new IfStatement("services".ToSimpleName().EqualTo(SimpleNameExpression.Null))
                .AddStatement(new ObjectCreationExpression(DataType.FromSymbol(
                        compilation.GetTypeByMetadataName("System.ArgumentNullException")!, compilation))
                    .AddArgument("services".ToLiteral()).Throw));

        for (var index = 0; index < model.Services.Count; index++)
        {
            var service = model.Services[index];
            if (service.Mode != "Replace") RegistrationEmitter.Add(service, index, compilation, type, entry);
        }

        foreach (var dependency in model.Dependencies)
            entry.AddStatement(DataType.FromSymbol(dependency.ContainingType, compilation).Type
                .Sub(dependency.Name.ToValidIdentifier()).Invoke("services".ToSimpleName()));

        var extensions = DataType.FromSymbol(compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")!, compilation);
        foreach (var removal in model.Removals)
        {
            var contract = (ITypeSymbol)removal.ConstructorArguments[0].Value!;
            var key = removal.NamedArguments.FirstOrDefault(argument => argument.Key == "Key").Value;
            entry.AddStatement(key.IsNull || key.Value is null
                ? extensions.Type.Sub("RemoveAll").Invoke("services".ToSimpleName(), RegistrationEmitter.TypeOf(contract, compilation))
                : extensions.Type.Sub("RemoveAllKeyed").Invoke("services".ToSimpleName(), RegistrationEmitter.TypeOf(contract, compilation), RegistrationEmitter.Constant(key, compilation)));
        }

        for (var index = 0; index < model.Services.Count; index++)
        {
            var service = model.Services[index];
            if (service.Mode == "Replace") RegistrationEmitter.Add(service, index, compilation, type, entry);
        }

        foreach (var configuration in model.Configurations)
            entry.AddStatement(DataType.FromSymbol(configuration.ContainingType, compilation).Type
                .Sub(configuration.Name.ToValidIdentifier()).Invoke("services".ToSimpleName()));

        entry.AddStatement("ConfigureServices".ToSimpleName().Invoke("services".ToSimpleName()));
        entry.AddStatement("services".ToSimpleName().Return);
        var hook = Method("ConfigureServices").Static.Partial.AddParameter(new Parameter(collection, "services"));
        hook.Accessibility = TedToolkit.RoslynHelper.Accessibility.NONE;
        type.AddMember(hook).AddMember(entry);
        var attribute = new TedToolkit.RoslynHelper.Syntaxes.Attribute(compilation.GetTypeByMetadataName(Names.MODULE_ATTRIBUTE)!, compilation)
            .AddArgument((Names.GENERATED_NAMESPACE + "." + model.TypeName).ToLiteral())
            .AddArgument(model.MethodName.ToLiteral());
        var file = File();
        file.DisableWarnings = false;
        file.NullableContext = NullableContextOptions.Enable;
        file.AddAttribute(attribute).AddNameSpace(NameSpace(Names.GENERATED_NAMESPACE).AddMember(type));
        file.Generate(context, model.TypeName);
    }
}
