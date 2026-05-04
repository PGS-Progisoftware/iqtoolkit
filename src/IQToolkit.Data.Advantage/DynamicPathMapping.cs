using IQToolkit.Data.Common;
using System;
using System.Collections.Generic;

namespace IQToolkit.Data.Advantage
{
    public class DynamicPathMapping : AdvantageMapping
    {
        private readonly Dictionary<Type, string> _tablePaths;

        public DynamicPathMapping(Dictionary<Type, string> tablePaths = null) : base()
        {
            _tablePaths = tablePaths ?? new Dictionary<Type, string>();
        }

        public override string GetTableName(MappingEntity entity)
        {
            string path;
            if (_tablePaths.TryGetValue(entity.StaticType, out path))
                return path;
            return base.GetTableName(entity);
        }
    }
}