﻿using System;
using System.Linq;
using SqlPad.Oracle.ToolTips;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle
{
	public class OracleToolTipProvider : IToolTipProvider
	{
		private static readonly OracleStatementValidator Validator = new OracleStatementValidator();

		public IToolTip GetToolTip(IDatabaseModel databaseModel, SqlDocument sqlDocument, int cursorPosition)
		{
			var node = sqlDocument.StatementCollection.GetNodeAtPosition(cursorPosition);
			if (node == null)
				return null;

			var tip = node.Type == NodeType.Terminal ? node.Id : null;

			var validationModel = (OracleValidationModel)Validator.BuildValidationModel(null, node.Statement, databaseModel);

			var nodeSemanticError = validationModel.GetNodesWithSemanticErrors().FirstOrDefault(n => node.HasAncestor(n.Key, true));
			if (nodeSemanticError.Key != null)
			{
				tip = nodeSemanticError.Value.ToolTipText;
			}
			else
			{
				var queryBlock = validationModel.SemanticModel.GetQueryBlock(node);

				switch (node.Id)
				{
					case Terminals.ObjectIdentifier:
						var objectReference = GetOracleObjectReference(queryBlock, node);
						if (objectReference == null)
							return null;

						var objectName = objectReference.Type == TableReferenceType.PhysicalObject
							? objectReference.SearchResult.SchemaObject.FullyQualifiedName
							: objectReference.FullyQualifiedName;
						tip = objectName + " (" + objectReference.Type.ToCategoryLabel() + ")";
						break;
					case Terminals.Min:
					case Terminals.Max:
					case Terminals.Sum:
					case Terminals.Avg:
					case Terminals.FirstValue:
					case Terminals.Count:
					case Terminals.Variance:
					case Terminals.StandardDeviation:
					case Terminals.LastValue:
					case Terminals.Lead:
					case Terminals.Lag:
					case Terminals.Identifier:
						var columnDescription = GetColumnDescription(queryBlock, node);
						if (columnDescription == null)
						{
							var functionName = GetFunctionName(queryBlock, node);
							if (functionName == null)
							{
								return null;
							}

							tip = functionName;
						}
						else
						{
							tip = columnDescription.Type + " (" + (columnDescription.Nullable ? String.Empty : "NOT ") + "NULL)";
						}
						
						break;
				}
			}

			return new ToolTipObject { DataContext = tip };
		}

		private string GetFunctionName(OracleQueryBlock queryBlock, StatementDescriptionNode terminal)
		{
			var functionReference = queryBlock.AllFunctionReferences.SingleOrDefault(f => f.FunctionIdentifierNode == terminal);
			return functionReference == null ? null : functionReference.Metadata.Identifier.FullyQualifiedIdentifier;
		}

		private OracleColumn GetColumnDescription(OracleQueryBlock queryBlock, StatementDescriptionNode terminal)
		{
			return queryBlock.AllColumnReferences
				.Where(c => c.ColumnNode == terminal)
				.Select(c => c.ColumnDescription)
				.FirstOrDefault();
		}

		private OracleObjectReference GetOracleObjectReference(OracleQueryBlock queryBlock, StatementDescriptionNode terminal)
		{
			var objectReference = queryBlock.AllColumnReferences
				.Where(c => c.ObjectNode == terminal && c.ColumnNodeObjectReferences.Count == 1)
				.Select(c => c.ColumnNodeObjectReferences.Single())
				.FirstOrDefault();

			if (objectReference != null)
				return objectReference;

			return queryBlock.ObjectReferences
				.FirstOrDefault(o => o.ObjectNode == terminal);
		}
	}
}