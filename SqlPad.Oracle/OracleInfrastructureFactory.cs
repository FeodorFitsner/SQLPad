﻿using System.Collections.Generic;
using System.Configuration;
using SqlPad.Commands;
using SqlPad.Oracle.Commands;

namespace SqlPad.Oracle
{
	public class OracleInfrastructureFactory : IInfrastructureFactory
	{
		private readonly OracleCommandFactory _commandFactory = new OracleCommandFactory();
		private readonly Dictionary<string, OracleDatabaseModel> _databaseModels = new Dictionary<string, OracleDatabaseModel>();

		#region Implementation of IInfrastructureFactory
		public ICommandFactory CommandFactory { get { return _commandFactory; } }
		
		public ITokenReader CreateTokenReader(string sqlText)
		{
			return OracleTokenReader.Create(sqlText);
		}

		public ISqlParser CreateSqlParser()
		{
			return new OracleSqlParser();
		}

		public IStatementValidator CreateStatementValidator()
		{
			return new OracleStatementValidator();
		}

		public IDatabaseModel CreateDatabaseModel(ConnectionStringSettings connectionString)
		{
			OracleDatabaseModel databaseModel;
			if (!_databaseModels.TryGetValue(connectionString.ConnectionString, out databaseModel))
			{
				_databaseModels[connectionString.ConnectionString] = databaseModel = new OracleDatabaseModel(connectionString);
			}
			else
			{
				databaseModel = databaseModel.Clone();
			}

			return databaseModel;
		}

		public ICodeCompletionProvider CreateCodeCompletionProvider()
		{
			return new OracleCodeCompletionProvider();
		}

		public ICodeSnippetProvider CreateSnippetProvider()
		{
			return new OracleSnippetProvider();
		}

		public IContextActionProvider CreateContextActionProvider()
		{
			return new OracleContextActionProvider();

		}

		public IMultiNodeEditorDataProvider CreateMultiNodeEditorDataProvider()
		{
			return new OracleMultiNodeEditorDataProvider();
		}

		public IStatementFormatter CreateSqlFormatter(SqlFormatterOptions options)
		{
			return new OracleStatementFormatter(options);
		}

		public IToolTipProvider CreateToolTipProvider()
		{
			return new OracleToolTipProvider();
		}

		public INavigationService CreateNavigationService()
		{
			return new OracleNavigationService();
		}
		#endregion
	}
}
