using IQToolkit.Data.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;

namespace IQToolkit.Data.Advantage
{
    public class AdvantageQueryProvider : DbEntityProvider
    {
        public bool EnableQueryTiming { get; set; } = true;

        public bool EnableInboundQueryLogging { get; set; }

        public Func<Expression, string> InboundQueryFormatter { get; set; } = FormatInboundExpression;

        private static string FormatInboundExpression(Expression expression)
        {
            if (expression == null)
            {
                return "(null)";
            }

            try
            {
                return AdvantageExpressionWriter.WriteToString(expression);
            }
            catch
            {
                try
                {
                    return expression.ToString();
                }
                catch (Exception ex)
                {
                    return $"(failed to format expression: {ex.Message})";
                }
            }
        }

		/// <summary>
		/// Logs the actual <see cref="DbCommand"/> (SQL + bound ADO parameter values) that will be
		/// sent to the Advantage ADO.NET provider. This is the most reliable form of logging since
		/// it reflects the final <see cref="DbParameter.Value"/> values.
		/// </summary>
		public bool EnableOutboundCommandLogging { get; set; } = true;

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

		public AdvantageQueryProvider(string connectionString)
			: this(CreateConnection(connectionString))
		{
		}

		public AdvantageQueryProvider(string connectionString, QueryPolicy policy = null)
            : this(CreateConnection(connectionString), new AdvantageMapping(), policy)
        {
        }

        public AdvantageQueryProvider(string connectionString, Dictionary<Type, string> tablePaths = null, QueryPolicy policy = null)
            : this(CreateConnection(connectionString), new DynamicPathMapping(tablePaths), policy)
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


			private static bool IsCharBackedEnumType(Type enumType)
			{
				enumType = TypeHelper.GetNonNullableType(enumType);
				if (!enumType.IsEnum)
				{
					return false;
				}

				return _charBackedEnumCache.GetOrAdd(enumType, t =>
					t.GetCustomAttributes(false).Cast<object>().Any(a => a.GetType().Name.Contains("CharBacked")));
			}

			private static object CoerceParameterValue(QueryParameter parameter, object value)
			{
				if (value == null || value == DBNull.Value)
				{
					return DBNull.Value;
				}

				var paramType = TypeHelper.GetNonNullableType(parameter.Type);
				if (!paramType.IsEnum)
				{
					return value;
				}

				// For CharBacked enums we want to bind the underlying character (CHAR(1)) value, not the enum name.
				// This is critical for updates/inserts, because parameter binding bypasses SQL formatter constant rendering.
				if (IsCharBackedEnumType(paramType))
				{
					// Value might already be an enum, or (rarely) already coerced to underlying integral.
					int intValue;
					if (value.GetType().IsEnum)
					{
						intValue = System.Convert.ToInt32(value);
					}
					else
					{
						intValue = System.Convert.ToInt32(value);
					}

					char ch = (char)intValue;
					return ch.ToString();
				}

				return value;
			}

			protected override void AddParameter(DbCommand command, QueryParameter parameter, object value)
			{
				DbParameter p = command.CreateParameter();
				p.ParameterName = parameter.Name;
				p.Value = CoerceParameterValue(parameter, value);
				command.Parameters.Add(p);
			}

			protected override void SetParameterValues(QueryCommand query, DbCommand command, object[] paramValues)
			{
				if (query.Parameters.Count > 0 && command.Parameters.Count == 0)
				{
					for (int i = 0, n = query.Parameters.Count; i < n; i++)
					{
						this.AddParameter(command, query.Parameters[i], paramValues != null ? paramValues[i] : null);
					}
				}
				else if (paramValues != null)
				{
					for (int i = 0, n = command.Parameters.Count; i < n; i++)
					{
						DbParameter p = command.Parameters[i];
						if (p.Direction == System.Data.ParameterDirection.Input
						 || p.Direction == System.Data.ParameterDirection.InputOutput)
						{
							p.Value = CoerceParameterValue(query.Parameters[i], paramValues[i]);
						}
					}
				}
			}

			private void LogOutboundCommand(DbCommand cmd)
			{
				if (!_provider.EnableOutboundCommandLogging || _provider.Log == null || cmd == null)
				{
					return;
				}

				_provider.Log.WriteLine("-- SQL (outbound)");
				_provider.Log.WriteLine(cmd.CommandText);

				for (int i = 0; i < cmd.Parameters.Count; i++)
				{
					var p = (DbParameter)cmd.Parameters[i];
					var v = p.Value;

					if (v == null || v == DBNull.Value)
					{
						_provider.Log.WriteLine("-- {0} = NULL", p.ParameterName);
					}
					else if (v is string s)
					{
						// Make whitespace visible (critical for space-backed enums).
						_provider.Log.WriteLine("-- {0} = ['{1}']", p.ParameterName, s);
					}
					else
					{
						_provider.Log.WriteLine("-- {0} = [{1}]", p.ParameterName, v);
					}
				}

				_provider.Log.WriteLine();
			}


			protected override void LogParameters(QueryCommand command, object[] paramValues)
			{
				if (_provider.Log == null || paramValues == null)
				{
					return;
				}

				for (int i = 0, n = command.Parameters.Count; i < n; i++)
				{
					var p = command.Parameters[i];
					var v = CoerceParameterValue(p, paramValues[i]);

					if (v == null || v == DBNull.Value)
					{
						_provider.Log.WriteLine("-- {0} = NULL", p.Name);
					}
					else if (v is string s)
					{
						// Make whitespace visible in logs (space-backed enums would otherwise look blank).
						_provider.Log.WriteLine("-- {0} = ['{1}']", p.Name, s);
					}
					else
					{
						_provider.Log.WriteLine("-- {0} = [{1}]", p.Name, v);
					}
				}
			}
			protected override DbCommand GetCommand(QueryCommand query, object[] paramValues)
			{
				var cmd = base.GetCommand(query, paramValues);
				LogOutboundCommand(cmd);
				return cmd;
			}

			/// <summary>
			/// ace64.dll returns ADS Error 1500 on the first-ever parameterized AdsPrepareSQLW
			/// call in a fresh IIS process because aicu64.dll (Unicode support) is absent from
			/// the deployment. After that single failure ace64.dll activates its ANSI fallback
			/// permanently for the process lifetime, so a one-shot retry is sufficient.
			/// The !isRetry guard ensures a genuine second 1500 (or any other error on retry)
			/// propagates unconditionally — this is not a blanket swallow.
			/// </summary>
			private static bool IsAdsError(Exception ex, int number)
			{
				if (ex.GetType().Name != "AdsException") return false;
				var prop = ex.GetType().GetProperty("Number");
				return prop?.GetValue(ex) is int code && code == number;
			}

			// ExecuteNonQuery path (INSERT/UPDATE/DELETE and parameterized non-queries).
			// Retry wraps above ExecuteWithTiming, so a cold-start 1500 will appear in the
			// log as "Query FAILED" before the retry "Query completed" — acceptable and informative.
			public override int ExecuteCommand(QueryCommand query, object[] paramValues)
			{
				for (bool isRetry = false; ; isRetry = true)
				{
					try
					{
						if (_provider.EnableQueryTiming && _provider.Log != null)
							return ExecuteWithTiming(query, paramValues, base.ExecuteCommand);
						return base.ExecuteCommand(query, paramValues);
					}
					catch (Exception ex) when (!isRetry && IsAdsError(ex, 1500))
					{
						_provider.Log?.WriteLine("ADS ICU cold-start Error 1500 — retrying once on ANSI fallback path");
					}
				}
			}

			public override IEnumerable<T> Execute<T>(QueryCommand query, Func<FieldReader, T> fnProjector, MappingEntity entity, object[] paramValues)
			{
				if (_provider.EnableQueryTiming && _provider.Log != null)
				{
					return ExecuteWithTiming(query, paramValues, (q, p) => base.Execute<T>(q, fnProjector, entity, p));
				}
				return base.Execute<T>(query, fnProjector, entity, paramValues);
			}

			// ExecuteReader is called by base.Execute<T> via virtual dispatch, so the retry
			// fires below ExecuteWithTiming — timing sees one clean execution, no failure log.
			protected override DbDataReader ExecuteReader(DbCommand command)
			{
				for (bool isRetry = false; ; isRetry = true)
				{
					try
					{
						return base.ExecuteReader(command);
					}
					catch (Exception ex) when (!isRetry && IsAdsError(ex, 1500))
					{
						_provider.Log?.WriteLine("ADS ICU cold-start Error 1500 — retrying once on ANSI fallback path");
					}
				}
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

        public override Expression GetExecutionPlan(Expression expression)
        {
            if (this.EnableInboundQueryLogging && this.Log != null)
            {
                try
                {
                    this.Log.WriteLine("-- LINQ (inbound)");
                    this.Log.WriteLine(this.InboundQueryFormatter != null
                        ? this.InboundQueryFormatter(expression)
                        : FormatInboundExpression(expression));
                    this.Log.WriteLine();
                }
                catch (Exception ex)
                {
                    this.Log.WriteLine($"-- LINQ (inbound) logging failed: {ex.Message}");
                    this.Log.WriteLine();
                }
            }

            return base.GetExecutionPlan(expression);
        }

        // Core CommandGatherer throws on CLR Block; keep workaround in Advantage only.
        public override string GetQueryText(Expression expression)
        {
            Expression plan = this.GetExecutionPlan(expression);
            var commands = BlockAwareCommandGatherer.Gather(plan);
            return string.Join("\n\n", commands.Select(c => c.CommandText));
        }

        private sealed class BlockAwareCommandGatherer : DbExpressionVisitor
        {
            private readonly List<QueryCommand> commands = new List<QueryCommand>();

            public static List<QueryCommand> Gather(Expression expression)
            {
                var gatherer = new BlockAwareCommandGatherer();
                gatherer.Visit(expression);
                return gatherer.commands;
            }

            protected override Expression Visit(Expression exp)
            {
                if (exp != null && exp.NodeType == ExpressionType.Block)
                {
                    var block = (BlockExpression)exp;
                    foreach (var expression in block.Expressions)
                    {
                        this.Visit(expression);
                    }
                    return exp;
                }

                return base.Visit(exp);
            }

            protected override Expression VisitConstant(ConstantExpression c)
            {
                if (c.Value is QueryCommand qc)
                {
                    this.commands.Add(qc);
                }
                return c;
            }
        }
	}
}
