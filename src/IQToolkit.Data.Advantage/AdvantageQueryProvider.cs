using IQToolkit.Data.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageQueryProvider : DbEntityProvider
    {
        public bool EnableQueryTiming { get; set; } = true;

        #region Factory Methods

        /// <summary>
        /// Creates a new AdvantageQueryProvider with the specified connection string.
        /// /// </summary>
        public static AdvantageQueryProvider Create(string connectionString, QueryPolicy policy = null)
        {
            return new AdvantageQueryProvider(connectionString, policy);
        }

        #endregion

        #region Constructors

        public AdvantageQueryProvider(string connectionString, QueryPolicy policy = null)
            : this(CreateConnection(connectionString), new AdvantageMapping(), policy)
        {
        }

        public AdvantageQueryProvider(DbConnection connection)
            : this(connection, new AdvantageMapping(), null)
        {
        }

        public AdvantageQueryProvider(DbConnection connection, QueryMapping mapping, QueryPolicy policy)
            : base(connection, new AdvantageLanguage(), mapping, policy)
        {
        }

        #endregion

        // Override the Executor to ensure parameters are handled correctly
        protected override QueryExecutor CreateExecutor()
        {
            return new AdvantageExecutor(this);
        }

        private static DbConnection CreateConnection(string connectionString)
        {
            var factory = AdvantageProviderFactory.Instance;
            var conn = factory.CreateConnection();
            conn.ConnectionString = connectionString;
            return conn;
        }

        class AdvantageExecutor : Executor
        {
            private readonly AdvantageQueryProvider _provider;
            private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> _charBackedEnumCache 
                = new System.Collections.Concurrent.ConcurrentDictionary<Type, bool>();

            public AdvantageExecutor(AdvantageQueryProvider provider) : base(provider) 
            { 
                _provider = provider;
            }

            public override object Convert(object value, Type type)
            {
                if (value == null)
                {
                    return TypeHelper.GetDefault(type);
                }

                type = TypeHelper.GetNonNullableType(type);
                Type vtype = value.GetType();

                // For string values: treat empty/whitespace as null (except for CharBacked enums, handled below)
                if (vtype == typeof(string))
                {
                    var str = (string)value;
                    if (string.IsNullOrWhiteSpace(str) && !type.IsEnum)
                    {
                        return TypeHelper.GetDefault(type);
                    }
                }

                if (type != vtype)
                {
                    if (type.IsEnum)
                    {
                        // Special handling for CharBacked enums
                        if (vtype == typeof(string))
                        {
                            var stringValue = (string)value;

                            // Check if this is a CharBacked enum (cached)
                            bool isCharBacked = _charBackedEnumCache.GetOrAdd(type, t => 
                                t.GetCustomAttributes(false).Cast<object>().Any(a => a.GetType().Name.Contains("CharBacked")));
                        
                            if (isCharBacked)
                            {
                                // For CharBacked enums: empty/whitespace from CHAR(1) => space character
                                if (string.IsNullOrWhiteSpace(stringValue))
                                {
                                    stringValue = " ";
                                }
                                
                                // CharBacked enums store the character value, not the enum name
                                // Convert the character to its integer value and use Enum.ToObject
                                if (stringValue.Length > 0)
                                {
                                    return Enum.ToObject(type, (int)stringValue[0]);
                                }
                            }
                        
                            // For non-CharBacked enums, parse by name
                            return Enum.Parse(type, stringValue);
                        }
                        else
                        {
                            Type utype = Enum.GetUnderlyingType(type);
                            if (utype != vtype)
                            {
                                value = System.Convert.ChangeType(value, utype);
                            }
                            return Enum.ToObject(type, value);
                        }
                    }

                    return System.Convert.ChangeType(value, type);
                }

                return value;
            }
			protected override DbCommand GetCommand(QueryCommand query, object[] paramValues)
			{
				return base.GetCommand(query, paramValues);
			}

			public override int ExecuteCommand(QueryCommand query, object[] paramValues)
			{
				if (_provider.EnableQueryTiming && _provider.Log != null)
				{
					return ExecuteWithTiming(query, paramValues, base.ExecuteCommand);
				}
				return base.ExecuteCommand(query, paramValues);
			}

			public override IEnumerable<T> Execute<T>(QueryCommand query, Func<FieldReader, T> fnProjector, MappingEntity entity, object[] paramValues)
			{
				if (_provider.EnableQueryTiming && _provider.Log != null)
				{
					return ExecuteWithTiming(query, paramValues, (q, p) => base.Execute<T>(q, fnProjector, entity, p));
				}
				return base.Execute<T>(query, fnProjector, entity, paramValues);
			}

			private TResult ExecuteWithTiming<TResult>(QueryCommand query, object[] paramValues, Func<QueryCommand, object[], TResult> executeFunc)
			{
				var stopwatch = Stopwatch.StartNew();
				
				try
				{
					stopwatch = Stopwatch.StartNew();
					var result = executeFunc(query, paramValues);
					stopwatch.Stop();
					
					// Log execution time
					_provider.Log.WriteLine($"Query completed in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedTicks} ticks)");					
					return result;
				}
				catch (Exception ex)
				{
					stopwatch.Stop();
					_provider.Log.WriteLine($"Query FAILED after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
					throw;
				}
			}

			protected override IEnumerable<T> Project<T>(DbDataReader reader, Func<FieldReader, T> fnProjector, MappingEntity entity, bool closeReader)
			{
				var freader = new AdvantageFieldReader(this, reader);
				try
				{
					while (reader.Read())
					{
						yield return fnProjector(freader);
					}
				}
				finally
				{
					if (closeReader)
					{
						((IDataReader)reader).Close();
					}
				}
			}
		}

		private class AdvantageFieldReader : DbFieldReader
		{
			public AdvantageFieldReader(Executor executor, DbDataReader reader)
				: base(executor, reader)
			{
			}

			protected override bool IsDBNull(int ordinal)
			{
				// Only check the actual database NULL value
				// Let Convert/GetString handle empty/whitespace string logic
				return base.IsDBNull(ordinal);
			}

			protected override string GetString(int ordinal)
			{
				var value = base.GetString(ordinal);
				
				// For Advantage: treat empty/whitespace strings as null
				if (string.IsNullOrWhiteSpace(value))
				{
					return null;
				}
				
				return value;
			}
		}
	}
}
