using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using SmartCodeGenerator.Engine.PluginArchitectureDemo;
using SmartCodeGenerator.Sdk;

namespace SmartCodeGenerator.Engine
{
    public class GeneratorPluginProvider
    {
        private readonly IReadOnlyDictionary<string,Lazy<ICodeGenerator>> _generators;

        public GeneratorPluginProvider(IReadOnlyList<string> generatorAssemblyPaths)
        {
            var generatorInterfaceType = typeof(ICodeGenerator);
            _generators =  generatorAssemblyPaths.SelectMany(x =>
            {
                var generatorLoadContext = new GeneratorLoadContext(x, typeof(ICodeGenerator).Assembly);
                var pluginAssembly = generatorLoadContext.LoadFromAssemblyPath(x);
                var generatorTypes = pluginAssembly.GetTypes().Where(t => generatorInterfaceType.IsAssignableFrom(t));
                return CreateGenerators(generatorTypes);
            }).ToDictionary(t => t.key, t => t.generator);
        }

        private static IEnumerable<(string key, Lazy<ICodeGenerator> generator)> CreateGenerators(IEnumerable<Type> generatorTypes)
        {
            foreach (var type in generatorTypes)
            {
                var generatorAttribute = (CodeGeneratorAttribute?)type.GetCustomAttribute(typeof(CodeGeneratorAttribute));
                var key = generatorAttribute?.ProcessMarkedWith.FullName;
                if (key == null)
                {
                    continue;
                }
                var generator = new Lazy<ICodeGenerator>(() => (ICodeGenerator)(Activator.CreateInstance(type)!));
                yield return (key, generator);
            }
        }

        private ICodeGenerator? FindFor(AttributeData attributeData)
        {
            var key = attributeData.AttributeClass.ToDisplayString();
            _generators.TryGetValue(key, out var generator);
            return generator?.Value;
        }

        public IEnumerable<(AttributeData, ICodeGenerator)> FindCodeGenerators(IReadOnlyCollection<AttributeData> nodeAttributes)
        {
            foreach (var attributeData in nodeAttributes)
            {
                var codeGenerator = this.FindFor(attributeData);
                if (codeGenerator != null)
                {
                    yield return (attributeData, codeGenerator);
                }
            }
        }
    }
}