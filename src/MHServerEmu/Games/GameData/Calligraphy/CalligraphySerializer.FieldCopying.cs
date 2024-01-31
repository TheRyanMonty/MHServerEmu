﻿using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    public partial class CalligraphySerializer
    {
        /// <summary>
        /// Copies field values from a <see cref="Prototype"/> with the specified data ref.
        /// </summary>
        private static bool CopyPrototypeDataRefFields(Prototype destPrototype, PrototypeId sourceDataRef)
        {
            // Check to make sure our reference is valid
            if (sourceDataRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, "Failed to copy prototype data ref fields: invalid source ref");

            // Get source prototype and copy fields from it
            Prototype sourcePrototype = GameDatabase.GetPrototype<Prototype>(sourceDataRef);
            return CopyPrototypeFields(destPrototype, sourcePrototype);
        }

        /// <summary>
        /// Copies all appropriate field values from one <see cref="Prototype"/> instance to another.
        /// </summary>
        private static bool CopyPrototypeFields(Prototype destPrototype, Prototype sourcePrototype)
        {
            // In some cases (e.g. PopulationInfoPrototype mixin) destination and/or source may be null
            if (destPrototype == null || sourcePrototype == null) return false;

            // Get type information for both prototypes and make sure they are the same
            Type destType = destPrototype.GetType();
            Type sourceType = sourcePrototype.GetType();

            if (sourceType != destType)
                return Logger.WarnReturn(false, $"Failed to copy prototype fields: source type ({sourceType.Name}) does not match destination type ({destType.Name})");

            foreach (var fieldInfo in destType.GetProperties())
            {
                if (fieldInfo.DeclaringType == typeof(Prototype)) continue;      // Skip base prototype properties
                
                switch (GameDatabase.PrototypeClassManager.GetPrototypeFieldTypeEnumValue(fieldInfo))
                {
                    case PrototypeFieldType.Bool:
                    case PrototypeFieldType.Int8:
                    case PrototypeFieldType.Int16:
                    case PrototypeFieldType.Int32:
                    case PrototypeFieldType.Int64:
                    case PrototypeFieldType.Float32:
                    case PrototypeFieldType.Float64:
                    case PrototypeFieldType.Enum:
                    case PrototypeFieldType.AssetRef:
                    case PrototypeFieldType.AssetTypeRef:
                    case PrototypeFieldType.CurveRef:
                    case PrototypeFieldType.PrototypeDataRef:
                    case PrototypeFieldType.LocaleStringId:
                    case PrototypeFieldType.PrototypePtr:
                    case PrototypeFieldType.PropertyId:
                        AssignPointedAtValues(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.ListBool:
                    case PrototypeFieldType.ListInt8:
                    case PrototypeFieldType.ListInt16:
                    case PrototypeFieldType.ListInt32:
                    case PrototypeFieldType.ListInt64:
                    case PrototypeFieldType.ListFloat32:
                    case PrototypeFieldType.ListFloat64:
                    case PrototypeFieldType.ListEnum:
                    case PrototypeFieldType.ListAssetRef:
                    case PrototypeFieldType.ListAssetTypeRef:
                    case PrototypeFieldType.ListPrototypeDataRef:
                    case PrototypeFieldType.ListPrototypePtr:
                        ShallowCopyCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.Mixin:
                        CopyMixin(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.ListMixin:
                        CopyMixinCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.PropertyList:
                    case PrototypeFieldType.PropertyCollection:
                        CopyPrototypePropertyCollection(destPrototype, sourcePrototype, fieldInfo);
                        break;

                    case PrototypeFieldType.Invalid: return false;
                    default: return Logger.WarnReturn(false, $"Trying to copy unhandled prototype field type {fieldInfo.PropertyType.Name}");
                }
            }

            return true;
        }

        /// <summary>
        /// Copies a field value from one <see cref="Prototype"/> instance to another.
        /// </summary>
        private static void AssignPointedAtValues(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            fieldInfo.SetValue(destPrototype, fieldInfo.GetValue(sourcePrototype));
        }

        /// <summary>
        /// Creates a shallow copy of a collection field from a source <see cref="Prototype"/> 
        /// </summary>
        private static void ShallowCopyCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var sourceData = (Array)fieldInfo.GetValue(sourcePrototype);
            if (sourceData == null) return;

            int numItems = sourceData.Length;
            var destData = Array.CreateInstance(fieldInfo.PropertyType.GetElementType(), numItems);
            Array.Copy(sourceData, destData, numItems);
            fieldInfo.SetValue(destPrototype, destData);
        }

        private static void CopyMixin(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            var destMixin = (Prototype)fieldInfo.GetValue(destPrototype);
            var sourceMixin = (Prototype)fieldInfo.GetValue(sourcePrototype);

            // Create the mixin instance on the destination prototype if it doesn't exist and there is something to copy
            if (sourceMixin != null && destMixin == null)
            {
                destMixin = (Prototype)Activator.CreateInstance(fieldInfo.PropertyType);
                fieldInfo.SetValue(destPrototype, destMixin);
            }

            CopyPrototypeFields(destMixin, sourceMixin);
        }

        private static void CopyMixinCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            // NYI
        }

        private static void CopyPrototypePropertyCollection(Prototype destPrototype, Prototype sourcePrototype, System.Reflection.PropertyInfo fieldInfo)
        {
            // NYI
        }
    }
}
