﻿using System.Collections.Generic;
using System.Linq;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle
{
	public static class OracleStatementSemanticModelFactory
	{
		public static IStatementSemanticModel Build(OracleDatabaseModelBase databaseModel, OracleStatement statement, string statementText)
		{
			return null;
		}
	}

	public class OraclePlSqlStatementSemanticModel : IStatementSemanticModel
	{
		private readonly OracleDatabaseModelBase _databaseModel;
		private readonly List<OraclePlSqlProgram> _programs = new List<OraclePlSqlProgram>();

		public IDatabaseModel DatabaseModel { get { return _databaseModel; } }
		
		public StatementBase Statement { get; private set; }
		
		public string StatementText { get; private set; }
		
		public bool IsSimpleModel { get { return DatabaseModel == null; } }
		
		public ICollection<RedundantTerminalGroup> RedundantSymbolGroups { get { return new RedundantTerminalGroup[0]; } }

		public OraclePlSqlStatementSemanticModel(OracleDatabaseModelBase databaseModel, OracleStatement statement, string statementText)
		{
			_databaseModel = databaseModel;
			Statement = statement;
			StatementText = statementText;

			Build();
		}

		public void Build()
		{
			ResolveProgramDeclarations();
			ResolveProgramBodies();
		}

		private void ResolveProgramBodies()
		{
			
		}

		private void ResolveProgramDeclarations()
		{
			var declarationSections = Statement.RootNode.GetDescendants(NonTerminals.ProgramDeclareSection);
		}
	}

	internal class OraclePlSqlProgram
	{
		public StatementGrammarNode RootNode { get; set; }
	}
}
