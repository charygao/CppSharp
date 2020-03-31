using System;
using System.Collections.Generic;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators;
using CppSharp.Generators.C;
using Type = CppSharp.AST.Type;

namespace CppSharp.Types
{
    public class TypeMapDatabase : ITypeMapDatabase
    {
        public IDictionary<string, TypeMap> TypeMaps { get; set; }
        private readonly BindingContext Context;

        public TypeMapDatabase(BindingContext bindingContext)
        {
            Context = bindingContext;
            TypeMaps = new Dictionary<string, TypeMap>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var types = assembly.FindDerivedTypes(typeof(TypeMap));
                    SetupTypeMaps(types, bindingContext);
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    Diagnostics.Error("Error loading type maps from assembly '{0}': {1}",
                        assembly.GetName().Name, ex.Message);
                }
            }
        }

        private void SetupTypeMaps(IEnumerable<System.Type> types,
            BindingContext bindingContext)
        {
            foreach (var type in types)
            {
                var attrs = type.GetCustomAttributes(typeof(TypeMapAttribute), true);
                foreach (TypeMapAttribute attr in attrs)
                {
                    if (attr.GeneratorKind == 0 ||
                        attr.GeneratorKind == bindingContext.Options.GeneratorKind)
                    {
                        var typeMap = (TypeMap) Activator.CreateInstance(type);
                        typeMap.Context = bindingContext;
                        typeMap.TypeMapDatabase = this;
                        this.TypeMaps[attr.Type] = typeMap;
                    }
                }
            }
        }
        
        readonly bool[] PrintTypeQualifiers = { true, false };
        readonly bool[] PrintTypeModifiers = { true, false };
        readonly bool[] ResolveTypedefs = { false, true };
        readonly TypePrintScopeKind[] TypePrintScopeKinds =
            { TypePrintScopeKind.Local, TypePrintScopeKind.Qualified };

        public bool FindTypeMap(Type type, out TypeMap typeMap)
        {
            // Looks up the type in the cache map.
            if (typeMaps.ContainsKey(type))
            {
                typeMap = typeMaps[type];
                typeMap.Type = type;
                return typeMap.IsEnabled;
            }

            var template = type as TemplateSpecializationType;
            if (template != null)
            {
                var specialization = template.GetClassTemplateSpecialization();
                if (specialization != null &&
                    FindTypeMap(specialization, out typeMap))
                    return true;

                if (template.Template.TemplatedDecl != null)
                {
                    if (FindTypeMap(template.Template.TemplatedDecl,
                        out typeMap))
                    {
                        typeMap.Type = type;
                        return true;
                    }

                    return false;
                }
            }

        search:

            var typePrinter = new CppTypePrinter(Context)
            {
                ResolveTypeMaps = false,
                PrintLogicalNames = true
            };

            foreach (var printTypeModifiers in PrintTypeModifiers)
            {
                foreach (var printTypeQualifiers in PrintTypeQualifiers)
                {
                    foreach (var resolveTypeDefs in ResolveTypedefs)
                    {
                        foreach (var typePrintScopeKind in TypePrintScopeKinds)
                        {
                            typePrinter.ScopeKind = typePrintScopeKind;
                            typePrinter.ResolveTypedefs = resolveTypeDefs;
                            typePrinter.PrintTypeQualifiers = printTypeQualifiers;
                            typePrinter.PrintTypeModifiers = printTypeModifiers;

                            if (FindTypeMap(type.Visit(typePrinter), out typeMap))
                            {
                                typeMap.Type = type;
                                typeMaps[type] = typeMap;
                                return true;
                            }
                        }
                    }
                }
            }

            Type desugared = type.Desugar();
            if (type != desugared)
            {
                type = desugared;
                goto search;
            }

            if (type is PointerType)
            {
                type = type.GetPointee();
                goto search;
            }

            typeMap = null;
            return false;
        }

        public bool FindTypeMap(Declaration declaration, out TypeMap typeMap) =>
            FindTypeMap(new TagType(declaration), out typeMap);

        public bool FindTypeMap(string name, out TypeMap typeMap) =>
            TypeMaps.TryGetValue(name, out typeMap) && typeMap.IsEnabled;

        private Dictionary<Type, TypeMap> typeMaps = new Dictionary<Type, TypeMap>();
    }
}
