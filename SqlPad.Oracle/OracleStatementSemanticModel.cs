using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle
{
	public class OracleStatementSemanticModel : IStatementSemanticModel
	{
		private static readonly string[] StandardIdentifierIds =
		{
			Terminals.Identifier,
			Terminals.RowIdPseudoColumn,
			Terminals.Count,
			Terminals.Level,
			Terminals.User,
			Terminals.NegationOrNull,
			NonTerminals.AggregateFunction,
			NonTerminals.AnalyticFunction,
			NonTerminals.WithinGroupAggregationFunction
		};

		private readonly List<OracleInsertTarget> _insertTargets = new List<OracleInsertTarget>();
		private readonly List<OracleLiteral> _literals = new List<OracleLiteral>();
		private readonly HashSet<StatementGrammarNode> _redundantTerminals = new HashSet<StatementGrammarNode>();
		private readonly List<RedundantTerminalGroup> _redundantTerminalGroups = new List<RedundantTerminalGroup>();
		private readonly OracleDatabaseModelBase _databaseModel;
		private readonly Dictionary<OracleSelectListColumn, ICollection<OracleDataObjectReference>> _asteriskTableReferences = new Dictionary<OracleSelectListColumn, ICollection<OracleDataObjectReference>>();
		private readonly Dictionary<OracleQueryBlock, IList<string>> _commonTableExpressionExplicitColumnNames = new Dictionary<OracleQueryBlock, IList<string>>();
		private readonly Dictionary<OracleQueryBlock, List<StatementGrammarNode>> _queryBlockTerminals = new Dictionary<OracleQueryBlock, List<StatementGrammarNode>>();
		private readonly Dictionary<OracleQueryBlock, ICollection<StatementGrammarNode>> _accessibleQueryBlockRoot = new Dictionary<OracleQueryBlock, ICollection<StatementGrammarNode>>();
		private readonly Dictionary<OracleDataObjectReference, ICollection<KeyValuePair<StatementGrammarNode, string>>> _objectReferenceCteRootNodes = new Dictionary<OracleDataObjectReference, ICollection<KeyValuePair<StatementGrammarNode, string>>>();
		private readonly HashSet<OracleQueryBlock> _unreferencedQueryBlocks = new HashSet<OracleQueryBlock>();
		
		private OracleQueryBlock _mainQueryBlock;
		private Dictionary<StatementGrammarNode, OracleQueryBlock> _queryBlockNodes = new Dictionary<StatementGrammarNode, OracleQueryBlock>();

		public OracleDatabaseModelBase DatabaseModel
		{
			get
			{
				if (_databaseModel == null)
				{
					throw new InvalidOperationException("This model does not include database model reference. ");
				}

				return _databaseModel;
			}
		}

		IDatabaseModel IStatementSemanticModel.DatabaseModel { get { return DatabaseModel; } }

		public OracleStatement Statement { get; private set; }

		StatementBase IStatementSemanticModel.Statement { get { return Statement; } }
		
		public string StatementText { get; private set; }
		
		public bool IsSimpleModel { get { return _databaseModel == null; } }

		public ICollection<OracleQueryBlock> QueryBlocks
		{
			get { return _queryBlockNodes.Values; }
		}

		public ICollection<OracleInsertTarget> InsertTargets { get { return _insertTargets; }}

		public ICollection<RedundantTerminalGroup> RedundantSymbolGroups { get { return _redundantTerminalGroups.AsReadOnly(); } } 

		public OracleMainObjectReferenceContainer MainObjectReferenceContainer { get; private set; }

		public IEnumerable<OracleReferenceContainer> AllReferenceContainers
		{
			get
			{
				return _queryBlockNodes.Values
					.SelectMany(qb => Enumerable.Repeat(qb, 1).Concat(qb.ChildContainers))
					.Concat(_insertTargets)
					.Concat(Enumerable.Repeat(MainObjectReferenceContainer, 1));
			}
		}

		public OracleQueryBlock MainQueryBlock
		{
			get
			{
				return _mainQueryBlock ??
				       (_mainQueryBlock = _queryBlockNodes.Values
					       .Where(qb => qb.Type == QueryBlockType.Normal)
					       .OrderBy(qb => qb.RootNode.Level)
					       .FirstOrDefault());
			}
		}

		public IEnumerable<OracleLiteral> Literals
		{
			get { return _literals; }
		}

		public OracleStatementSemanticModel(string statementText, OracleStatement statement) : this(statement, null)
		{
			StatementText = statementText;
		}

		public OracleStatementSemanticModel(string statementText, OracleStatement statement, OracleDatabaseModelBase databaseModel)
			: this(statement, databaseModel)
		{
			if (databaseModel == null)
				throw new ArgumentNullException("databaseModel");

			StatementText = statementText;
		}

		private OracleStatementSemanticModel(OracleStatement statement, OracleDatabaseModelBase databaseModel)
		{
			if (statement == null)
				throw new ArgumentNullException("statement");

			Statement = statement;
			_databaseModel = databaseModel;

			MainObjectReferenceContainer = new OracleMainObjectReferenceContainer(this);

			if (statement.RootNode == null)
				return;

			Build();
		}

		private void Initialize()
		{
			var queryBlocks = new List<OracleQueryBlock>();
			var queryBlockTerminalListQueue = new Stack<KeyValuePair<OracleQueryBlock, List<StatementGrammarNode>>>();
			var queryBlockTerminalList = new KeyValuePair<OracleQueryBlock, List<StatementGrammarNode>>();
			
			foreach (var terminal in Statement.AllTerminals)
			{
				if (String.Equals(terminal.Id, Terminals.Select) && String.Equals(terminal.ParentNode.Id, NonTerminals.QueryBlock))
				{
					var queryBlock = new OracleQueryBlock(this) { RootNode = terminal.ParentNode, Statement = Statement };
					queryBlocks.Add(queryBlock);

					if (queryBlockTerminalList.Key != null)
					{
						queryBlockTerminalListQueue.Push(queryBlockTerminalList);
					}

					queryBlockTerminalList = new KeyValuePair<OracleQueryBlock, List<StatementGrammarNode>>(queryBlock, new List<StatementGrammarNode>());
					_queryBlockTerminals.Add(queryBlock, queryBlockTerminalList.Value);
				}
				else if (String.Equals(terminal.ParentNode.Id, NonTerminals.Expression) &&
				         (String.Equals(terminal.Id, Terminals.Date) || String.Equals(terminal.Id, Terminals.Timestamp)))
				{
					var literal = CreateLiteral(terminal);
					if (literal.Terminal != null && String.Equals(literal.Terminal.Id, Terminals.StringLiteral))
					{
						_literals.Add(literal);
					}
				}

				if (queryBlockTerminalList.Key != null)
				{
					queryBlockTerminalList.Value.Add(terminal);

					if (terminal == queryBlockTerminalList.Key.RootNode.LastTerminalNode && queryBlockTerminalListQueue.Count > 0)
					{
						queryBlockTerminalList = queryBlockTerminalListQueue.Pop();
					}
				}
			}

			_queryBlockNodes = queryBlocks
				.OrderByDescending(q => q.RootNode.Level)
				.ToDictionary(qb => qb.RootNode);
		}

		private void Build()
		{
			Initialize();

			foreach (var kvp in _queryBlockNodes)
			{
				var queryBlockRoot = kvp.Key;
				var queryBlock = kvp.Value;

				var scalarSubqueryExpression = queryBlockRoot.GetAncestor(NonTerminals.Expression);
				if (scalarSubqueryExpression != null)
				{
					queryBlock.Type = QueryBlockType.ScalarSubquery;
				}

				var commonTableExpression = queryBlockRoot.GetPathFilterAncestor(NodeFilters.BreakAtNestedQueryBoundary, NonTerminals.CommonTableExpression);
				if (commonTableExpression != null)
				{
					queryBlock.AliasNode = commonTableExpression.ChildNodes[0];
					queryBlock.Type = QueryBlockType.CommonTableExpression;
				}
				else
				{
					var selfTableReference = queryBlockRoot.GetAncestor(NonTerminals.TableReference);
					if (selfTableReference != null)
					{
						queryBlock.Type = QueryBlockType.Normal;
						queryBlock.AliasNode = selfTableReference.GetDescendantByPath(Terminals.ObjectAlias);
					}
				}

				queryBlock.HierarchicalQueryClause = queryBlockRoot.GetDescendantByPath(NonTerminals.HierarchicalQueryClause);
				queryBlock.FromClause = queryBlockRoot.GetDescendantByPath(NonTerminals.FromClause);
				var tableReferenceNonterminals = queryBlock.FromClause == null
					? Enumerable.Empty<StatementGrammarNode>()
					: queryBlock.FromClause.GetDescendantsWithinSameQuery(NonTerminals.TableReference).ToArray();

				// TODO: Check possible issue within GetCommonTableExpressionReferences to remove the Distinct.
				var cteReferences = GetCommonTableExpressionReferences(queryBlockRoot).Distinct().ToDictionary(qb => qb.Key, qb => qb.Value);
				_accessibleQueryBlockRoot.Add(queryBlock, cteReferences.Keys);

				foreach (var tableReferenceNonterminal in tableReferenceNonterminals)
				{
					var queryTableExpression = tableReferenceNonterminal.GetDescendantsWithinSameQuery(NonTerminals.QueryTableExpression).SingleOrDefault();
					if (queryTableExpression == null)
					{
						var specialTableReference = ResolveXmlTableReference(queryBlock, tableReferenceNonterminal) ?? ResolveJsonTableReference(queryBlock, tableReferenceNonterminal);

						if (specialTableReference != null)
						{
							specialTableReference.RootNode = tableReferenceNonterminal;
							specialTableReference.AliasNode = tableReferenceNonterminal.GetDescendantByPath(NonTerminals.ObjectAsAlias, Terminals.ObjectAlias);
							queryBlock.ObjectReferences.Add(specialTableReference);
						}
					}
					else
					{
						var objectReferenceAlias = tableReferenceNonterminal.GetDescendantByPath(Terminals.ObjectAlias);
						var nestedQueryTableReference = queryTableExpression.GetPathFilterDescendants(f => f.Id != NonTerminals.Subquery, NonTerminals.NestedQuery).SingleOrDefault();
						if (nestedQueryTableReference != null)
						{
							var nestedQueryTableReferenceQueryBlock = nestedQueryTableReference.GetPathFilterDescendants(n => n.Id != NonTerminals.NestedQuery && n.Id != NonTerminals.SubqueryFactoringClause, NonTerminals.QueryBlock).FirstOrDefault();
							if (nestedQueryTableReferenceQueryBlock != null)
							{
								queryBlock.ObjectReferences.Add(
									new OracleDataObjectReference(ReferenceType.InlineView)
									{
										Owner = queryBlock,
										RootNode = tableReferenceNonterminal,
										ObjectNode = nestedQueryTableReferenceQueryBlock,
										AliasNode = objectReferenceAlias
									});
							}

							continue;
						}

						var objectIdentifierNode = queryTableExpression.GetDescendantByPath(Terminals.ObjectIdentifier);
						var databaseLinkNode = GetDatabaseLinkFromQueryTableExpression(queryTableExpression);
						if (objectIdentifierNode == null)
						{
							var tableCollection = queryTableExpression.GetDescendantByPath(NonTerminals.TableCollectionExpression);
							if (tableCollection != null)
							{
								var functionIdentifierNode = tableCollection.GetDescendants(Terminals.Identifier).FirstOrDefault();
								if (functionIdentifierNode != null)
								{
									var prefixNonTerminal = functionIdentifierNode.ParentNode.GetDescendantByPath(NonTerminals.Prefix);
									var functionCallNodes = GetFunctionCallNodes(functionIdentifierNode);
									var tableCollectionProgramReference = CreateProgramReference(queryBlock, queryBlock, null, StatementPlacement.TableReference, functionIdentifierNode, prefixNonTerminal, functionCallNodes);
									tableCollectionProgramReference.RootNode = functionIdentifierNode.ParentNode;

									var tableCollectionDataObjectReference =
										new OracleTableCollectionReference(tableCollectionProgramReference)
										{
											Owner = queryBlock,
											Placement = StatementPlacement.TableReference,
											AliasNode = objectReferenceAlias,
											DatabaseLinkNode = databaseLinkNode,
											RootNode = tableReferenceNonterminal
										};

									if (tableCollectionDataObjectReference.DatabaseLinkNode == null)
									{
										var metadata = UpdateFunctionReferenceWithMetadata(tableCollectionProgramReference);
										if (metadata != null)
										{
											tableCollectionDataObjectReference.SchemaObject = tableCollectionProgramReference.SchemaObject;
											tableCollectionDataObjectReference.OwnerNode = tableCollectionProgramReference.OwnerNode;
											tableCollectionDataObjectReference.ObjectNode = tableCollectionProgramReference.ObjectNode;
										}
									}
									
									queryBlock.ProgramReferences.Add(tableCollectionProgramReference);
									queryBlock.ObjectReferences.Add(tableCollectionDataObjectReference);

									var identifiers = functionCallNodes.SelectMany(n => n.GetDescendantsWithinSameQuery(Terminals.Identifier));
									ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, StatementPlacement.TableReference, null);
								}
							}
						}
						else
						{
							ICollection<KeyValuePair<StatementGrammarNode, string>> commonTableExpressions = new Dictionary<StatementGrammarNode, string>();
							var schemaTerminal = queryTableExpression.GetDescendantByPath(NonTerminals.SchemaPrefix);
							if (schemaTerminal != null)
							{
								schemaTerminal = schemaTerminal.ChildNodes[0];
							}

							var tableName = objectIdentifierNode.Token.Value.ToQuotedIdentifier();
							if (schemaTerminal == null)
							{
								commonTableExpressions.AddRange(cteReferences.Where(n => n.Value == tableName));
							}

							OracleSchemaObject localSchemaObject = null;
							var referenceType = ReferenceType.CommonTableExpression;
							if (commonTableExpressions.Count == 0)
							{
								referenceType = ReferenceType.SchemaObject;

								var objectName = objectIdentifierNode.Token.Value;
								var owner = schemaTerminal == null ? null : schemaTerminal.Token.Value;

								if (!IsSimpleModel)
								{
									localSchemaObject = _databaseModel.GetFirstSchemaObject<OracleDataObject>(_databaseModel.GetPotentialSchemaObjectIdentifiers(owner, objectName));
								}
							}

							var objectReference =
								new OracleDataObjectReference(referenceType)
								{
									Owner = queryBlock,
									RootNode = tableReferenceNonterminal,
									OwnerNode = schemaTerminal,
									ObjectNode = objectIdentifierNode,
									DatabaseLinkNode = databaseLinkNode,
									AliasNode = objectReferenceAlias,
									SchemaObject = databaseLinkNode == null ? localSchemaObject : null
								};

							queryBlock.ObjectReferences.Add(objectReference);

							if (commonTableExpressions.Count > 0)
							{
								_objectReferenceCteRootNodes[objectReference] = commonTableExpressions;
							}

							FindExplicitPartitionReferences(queryTableExpression, objectReference);
						}
					}
				}

				FindSelectListReferences(queryBlock);

				FindWhereGroupByHavingReferences(queryBlock);

				FindJoinColumnReferences(queryBlock);

				FindHierarchicalClauseReferences(queryBlock);
			}

			ResolveInlineViewOrCommonTableExpressionRelations();

			FindRecursiveQueryReferences();

			ResolveModelClause();

			ExposeAsteriskColumns();

			ApplyExplicitCommonTableExpressionColumnNames();

			ResolveReferences();

			BuildDmlModel();
			
			ResolveRedundantTerminals();
		}

		private static void FindExplicitPartitionReferences(StatementGrammarNode queryTableExpression, OracleDataObjectReference objectReference)
		{
			var explicitPartitionIdentifier = queryTableExpression.GetDescendantByPath(NonTerminals.PartitionOrDatabaseLink, NonTerminals.PartitionExtensionClause, NonTerminals.PartitionNameOrKeySet, Terminals.ObjectIdentifier);
			if (explicitPartitionIdentifier == null)
			{
				return;
			}

			var table = (OracleTable)objectReference.SchemaObject.GetTargetSchemaObject();

			objectReference.PartitionReference =
				new OraclePartitionReference
				{
					RootNode = explicitPartitionIdentifier.ParentNode.ParentNode,
					ObjectNode = explicitPartitionIdentifier,
				};

			var partitionName = objectReference.PartitionReference.Name.ToQuotedIdentifier();
			if (table == null)
			{
				return;
			}

			var isSubPartition = objectReference.PartitionReference.RootNode.FirstTerminalNode.Id == Terminals.Subpartition;
			if (isSubPartition)
			{
				OracleSubPartition subPartition = null;
				table.Partitions.Values.FirstOrDefault(p => p.SubPartitions.TryGetValue(partitionName, out subPartition));
				objectReference.PartitionReference.Partition = subPartition;
			}
			else
			{
				OraclePartition partition;
				table.Partitions.TryGetValue(partitionName, out partition);
				objectReference.PartitionReference.Partition = partition;
			}

			objectReference.PartitionReference.DataObjectReference = objectReference;
		}

		private void FindRecursiveQueryReferences()
		{
			foreach (var queryBlock in _queryBlockNodes.Values.Where(qb => qb.Type == QueryBlockType.CommonTableExpression))
			{
				FindRecusiveSearchReferences(queryBlock);
				FindRecusiveCycleReferences(queryBlock);
			}
		}

		private void FindRecusiveCycleReferences(OracleQueryBlock queryBlock)
		{
			var subqueryComponentNode = queryBlock.RootNode.GetAncestor(NonTerminals.CommonTableExpression);
			queryBlock.RecursiveCycleClause = subqueryComponentNode.GetDescendantByPath(NonTerminals.SubqueryFactoringCycleClause);
			if (queryBlock.RecursiveCycleClause == null)
			{
				return;
			}

			var identifierListNode = queryBlock.RecursiveCycleClause.GetDescendantByPath(NonTerminals.IdentifierList);
			if (identifierListNode == null)
			{
				return;
			}

			var recursiveCycleClauseIdentifiers = identifierListNode.GetDescendantsWithinSameQuery(Terminals.Identifier);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, recursiveCycleClauseIdentifiers, StatementPlacement.RecursiveSearchOrCycleClause, null);

			var herarchicalQueryClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(identifierListNode);
			CreateGrammarSpecificFunctionReferences(herarchicalQueryClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.RecursiveSearchOrCycleClause, null);

			var cycleColumnAlias = queryBlock.RecursiveCycleClause.GetDescendantByPath(Terminals.ColumnAlias);
			if (cycleColumnAlias == null || !queryBlock.IsRecursive)
			{
				return;
			}

			var recursiveSequenceColumn =
				new OracleSelectListColumn(this, null)
				{
					Owner = queryBlock,
					RootNode = cycleColumnAlias,
					AliasNode = cycleColumnAlias,
					ColumnDescription =
						new OracleColumn
						{
							Name = cycleColumnAlias.Token.Value.ToQuotedIdentifier(),
							DataType = new OracleDataType { Length = 1, FullyQualifiedName = new OracleObjectIdentifier(String.Empty, "VARCHAR2") }
						}
				};

			queryBlock.AddAttachedColumn(recursiveSequenceColumn);
		}

		private void FindRecusiveSearchReferences(OracleQueryBlock queryBlock)
		{
			var subqueryComponentNode = queryBlock.RootNode.GetAncestor(NonTerminals.CommonTableExpression);
			queryBlock.RecursiveSearchClause = subqueryComponentNode.GetDescendantByPath(NonTerminals.SubqueryFactoringSearchClause);
			if (queryBlock.RecursiveSearchClause == null)
			{
				return;
			}

			var orderExpressionListNode = queryBlock.RecursiveSearchClause.GetDescendantByPath(NonTerminals.OrderExpressionList);
			if (orderExpressionListNode == null)
			{
				return;
			}

			var recursiveSearchClauseIdentifiers = orderExpressionListNode.GetDescendantsWithinSameQuery(Terminals.Identifier);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, recursiveSearchClauseIdentifiers, StatementPlacement.RecursiveSearchOrCycleClause, null);

			var herarchicalQueryClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(orderExpressionListNode);
			CreateGrammarSpecificFunctionReferences(herarchicalQueryClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.RecursiveSearchOrCycleClause, null);

			if (queryBlock.RecursiveSearchClause.LastTerminalNode.Id != Terminals.ColumnAlias || !queryBlock.IsRecursive)
			{
				return;
			}

			var recursiveSequenceColumn =
				new OracleSelectListColumn(this, null)
				{
					Owner = queryBlock,
					RootNode = queryBlock.RecursiveSearchClause.LastTerminalNode,
					AliasNode = queryBlock.RecursiveSearchClause.LastTerminalNode,
					ColumnDescription =
						new OracleColumn
						{
							Name = queryBlock.RecursiveSearchClause.LastTerminalNode.Token.Value.ToQuotedIdentifier(),
							DataType = OracleDataType.NumberType
						}
				};

			queryBlock.AddAttachedColumn(recursiveSequenceColumn);
		}

		private OracleSpecialTableReference ResolveJsonTableReference(OracleQueryBlock queryBlock, StatementGrammarNode tableReferenceNonterminal)
		{
			var jsonTableClause = tableReferenceNonterminal.GetDescendantsWithinSameQuery(NonTerminals.JsonTableClause).SingleOrDefault();
			if (jsonTableClause == null)
			{
				return null;
			}

			var columns = new List<OracleColumn>();
			foreach (var jsonTableColumn in jsonTableClause.GetDescendants(NonTerminals.JsonColumnDefinition).Where(n => n.TerminalCount >= 1 && n.FirstTerminalNode.Id == Terminals.ColumnAlias))
			{
				var columnAlias = jsonTableColumn.FirstTerminalNode.Token.Value.ToQuotedIdentifier();
				var column =
					new OracleColumn
					{
						Name = columnAlias,
						Nullable = true
					};

				columns.Add(column);

				if (!TryAssingnColumnForOrdinality(column, jsonTableColumn.ChildNodes.Skip(1)))
				{
					var jsonReturnTypeNode = jsonTableColumn[1];
					column.DataType = OracleDataType.FromJsonReturnTypeNode(jsonReturnTypeNode);
					if (column.DataType.FullyQualifiedName.Name == "VARCHAR2" && column.DataType.Length == null)
					{
						string maxStringSize;
						if (!DatabaseModel.SystemParameters.TryGetValue(OracleDatabaseModelBase.SystemParameterNameMaxStringSize, out maxStringSize) ||
						    maxStringSize == "STANDARD")
						{
							column.DataType.Length = 4000;
						}
						else
						{
							column.DataType.Length = 32767;
						}
					}
				}
			}

			var inputExpression = jsonTableClause.GetDescendantByPath(NonTerminals.Expression);
			if (inputExpression != null)
			{
				var identifiers = GetIdentifiers(inputExpression, Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.User);
				ResolveColumnAndFunctionReferenceFromIdentifiers(null, queryBlock, identifiers, StatementPlacement.TableReference, null);
			}

			return new OracleSpecialTableReference(ReferenceType.JsonTable, columns);
		}

		private OracleSpecialTableReference ResolveXmlTableReference(OracleQueryBlock queryBlock, StatementGrammarNode tableReferenceNonterminal)
		{
			var xmlTableClause = tableReferenceNonterminal.GetDescendantsWithinSameQuery(NonTerminals.XmlTableClause).SingleOrDefault();
			if (xmlTableClause == null)
			{
				return null;
			}

			var xmlTableOptions = xmlTableClause.GetDescendantByPath(NonTerminals.XmlTableOptions);
			if (xmlTableOptions == null)
			{
				return null;
			}

			var columns = new List<OracleColumn>();

			var columnListClause = xmlTableOptions.GetDescendantByPath(NonTerminals.XmlTableColumnListClause);
			if (columnListClause == null)
			{
				var column = OracleDatabaseModelBase.BuildColumnValueColumn(OracleDataType.XmlType);
				columns.Add(column);
			}
			else
			{
				foreach (var xmlTableColumn in columnListClause.GetDescendants(NonTerminals.XmlTableColumn).Where(n => n.TerminalCount >= 1 && n.FirstTerminalNode.Id == Terminals.ColumnAlias))
				{
					var columnAlias = xmlTableColumn.ChildNodes[0];

					var column =
						new OracleColumn
						{
							Name = columnAlias.Token.Value.ToQuotedIdentifier(),
							Nullable = true,
							DataType = OracleDataType.Empty
						};

					columns.Add(column);

					var xmlTableColumnDefinition = xmlTableColumn.GetDescendantByPath(NonTerminals.XmlTableColumnDefinition);
					if (xmlTableColumnDefinition != null && !TryAssingnColumnForOrdinality(column, xmlTableColumnDefinition.ChildNodes))
					{
						var dataTypeOrXmlTypeNode = xmlTableColumnDefinition.GetDescendantByPath(NonTerminals.DataTypeOrXmlType);
						if (dataTypeOrXmlTypeNode != null)
						{
							var dataTypeNode = dataTypeOrXmlTypeNode.ChildNodes[0];
							switch (dataTypeNode.Id)
							{
								case Terminals.XmlType:
									column.DataType = OracleDataType.XmlType;
									break;
								case NonTerminals.DataType:
									column.DataType = OracleDataType.FromDataTypeNode(dataTypeNode);
									break;
							}
						}
					}
				}
			}

			var xmlTablePassingClause = xmlTableOptions.GetDescendantByPath(NonTerminals.XmlPassingClause, NonTerminals.ExpressionAsXmlAliasWithMandatoryAsList);
			if (xmlTablePassingClause != null)
			{
				var identifiers = GetIdentifiers(xmlTablePassingClause, Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.User);
				ResolveColumnAndFunctionReferenceFromIdentifiers(null, queryBlock, identifiers, StatementPlacement.TableReference, null);
			}

			return new OracleSpecialTableReference(ReferenceType.XmlTable, columns);
		}

		private static bool TryAssingnColumnForOrdinality(OracleColumn column, IEnumerable<StatementGrammarNode> forOrdinalityTerminals)
		{
			var enumerator = forOrdinalityTerminals.GetEnumerator();
			if (enumerator.MoveNext() && enumerator.Current.Id == Terminals.For &&
			    enumerator.MoveNext() && enumerator.Current.Id == Terminals.Ordinality &&
			    !enumerator.MoveNext())
			{
				column.DataType = OracleDataType.NumberType;
				return true;
			}

			return false;
		}

		private void FindHierarchicalClauseReferences(OracleQueryBlock queryBlock)
		{
			if (queryBlock.HierarchicalQueryClause == null)
			{
				return;
			}

			var herarchicalQueryClauseIdentifiers = queryBlock.HierarchicalQueryClause.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, herarchicalQueryClauseIdentifiers, StatementPlacement.ConnectBy, null);

			var herarchicalQueryClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(queryBlock.HierarchicalQueryClause);
			CreateGrammarSpecificFunctionReferences(herarchicalQueryClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.ConnectBy, null);
		}

		private void ResolveModelClause()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				var modelClause = queryBlock.RootNode.GetDescendantByPath(NonTerminals.ModelClause);
				if (modelClause == null)
				{
					continue;
				}

				var modelColumnClauses = modelClause.GetDescendantByPath(NonTerminals.MainModel, NonTerminals.ModelColumnClauses);
				if (modelColumnClauses == null || modelColumnClauses.ChildNodes.Count < 5)
				{
					continue;
				}

				var partitionExpressions = modelColumnClauses.GetDescendantByPath(NonTerminals.ModelColumnClausesPartitionByExpressionList, NonTerminals.ParenthesisEnclosedAliasedExpressionList);
				var sqlModelColumns = new List<OracleSelectListColumn>();
				if (partitionExpressions != null)
				{
					sqlModelColumns.AddRange(GatherSqlModelColumns(queryBlock.ObjectReferences, partitionExpressions));
				}

				var dimensionExpressionList = modelColumnClauses.ChildNodes[modelColumnClauses.ChildNodes.Count - 3];
				var dimensionColumns = GatherSqlModelColumns(queryBlock.ObjectReferences, dimensionExpressionList);
				var dimensionColumnObjectReference = new OracleSpecialTableReference(ReferenceType.SqlModel, dimensionColumns.Select(c => c.ColumnDescription));
				sqlModelColumns.AddRange(dimensionColumns);
				
				var measureParenthesisEnclosedAliasedExpressionList = modelColumnClauses.ChildNodes[modelColumnClauses.ChildNodes.Count - 1];
				var measureColumns = GatherSqlModelColumns(queryBlock.ObjectReferences, measureParenthesisEnclosedAliasedExpressionList);
				sqlModelColumns.AddRange(measureColumns);

				queryBlock.ModelReference =
					new OracleSqlModelReference(this, sqlModelColumns, queryBlock.ObjectReferences)
					{
						Owner = queryBlock,
						RootNode = modelClause,
						MeasureExpressionList = measureParenthesisEnclosedAliasedExpressionList
					};

				var ruleDimensionIdentifiers = new List<StatementGrammarNode>();
				var ruleMeasureIdentifiers = new List<StatementGrammarNode>();
				var modelRulesClauseAssignmentList = modelColumnClauses.ParentNode.GetDescendantByPath(NonTerminals.ModelRulesClause, NonTerminals.ModelRulesClauseAssignmentList);
				if (modelRulesClauseAssignmentList == null)
				{
					continue;
				}

				foreach (var modelRulesClauseAssignment in modelRulesClauseAssignmentList.GetDescendants(NonTerminals.ModelRulesClauseAssignment))
				{
					var cellAssignment = modelRulesClauseAssignment.GetDescendantByPath(NonTerminals.CellAssignment);
					var assignmentDimensionList = cellAssignment.GetDescendantByPath(NonTerminals.MultiColumnForLoopOrConditionOrExpressionOrSingleColumnForLoopList);
					if (assignmentDimensionList != null)
					{
						ruleDimensionIdentifiers.AddRange(GetIdentifiers(assignmentDimensionList));
					}
					
					ruleMeasureIdentifiers.Add(cellAssignment.FirstTerminalNode);

					var assignmentExpression = modelRulesClauseAssignment.GetDescendantByPath(NonTerminals.Expression);
					if (assignmentExpression == null)
					{
						continue;
					}
					
					foreach (var identifier in GetIdentifiers(assignmentExpression))
					{
						if (identifier.GetPathFilterAncestor(n => n.Id != NonTerminals.ModelRulesClauseAssignment, NonTerminals.ConditionOrExpressionList) == null)
						{
							ruleMeasureIdentifiers.Add(identifier);
						}
						else
						{
							ruleDimensionIdentifiers.Add(identifier);
						}
					}
				}

				queryBlock.ModelReference.DimensionReferenceContainer.ObjectReferences.Add(dimensionColumnObjectReference);
				ResolveSqlModelReferences(queryBlock.ModelReference.DimensionReferenceContainer, ruleDimensionIdentifiers);

				queryBlock.ModelReference.MeasuresReferenceContainer.ObjectReferences.Add(queryBlock.ModelReference);
				ResolveSqlModelReferences(queryBlock.ModelReference.MeasuresReferenceContainer, ruleMeasureIdentifiers);

				queryBlock.ObjectReferences.Clear();
				queryBlock.ObjectReferences.Add(queryBlock.ModelReference);

				foreach (var column in queryBlock.AsteriskColumns)
				{
					if (column.RootNode.TerminalCount == 1)
					{
						_asteriskTableReferences[column] = new[] { queryBlock.ModelReference };
						break;
					}
					
					_asteriskTableReferences.Remove(column);
				}
			}
		}

		private void ResolveSqlModelReferences(OracleReferenceContainer referenceContainer, ICollection<StatementGrammarNode> identifiers)
		{
			ResolveColumnAndFunctionReferenceFromIdentifiers(null, referenceContainer, identifiers.Where(t => t.Id.In(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level)), StatementPlacement.Model, null);
			var grammarSpecificFunctions = identifiers.Where(t => t.Id.In(Terminals.Count, NonTerminals.AggregateFunction));
			CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, null, referenceContainer.ProgramReferences, StatementPlacement.Model, null);
			
			ResolveColumnObjectReferences(referenceContainer.ColumnReferences, referenceContainer.ObjectReferences, new OracleDataObjectReference[0]);
			ResolveFunctionReferences(referenceContainer.ProgramReferences);
		}

		private List<OracleSelectListColumn> GatherSqlModelColumns(ICollection<OracleDataObjectReference> objectReferences, StatementGrammarNode parenthesisEnclosedAliasedExpressionList)
		{
			var measureColumns = new List<OracleSelectListColumn>();

			foreach (var aliasedExpression in parenthesisEnclosedAliasedExpressionList.GetDescendants(NonTerminals.AliasedExpression))
			{
				var sqlModelColumn =
					new OracleSelectListColumn(this, null)
					{
						RootNode = aliasedExpression,
						AliasNode = aliasedExpression.GetDescendantByPath(NonTerminals.ColumnAsAlias, Terminals.ColumnAlias)
					};

				var lastnode = aliasedExpression.ChildNodes[aliasedExpression.ChildNodes.Count - 1];
				var aliasTerminalOffset = lastnode.Id == NonTerminals.ColumnAsAlias ? lastnode.TerminalCount : 0;
				if (aliasedExpression.TerminalCount - aliasTerminalOffset == 1 && aliasedExpression.FirstTerminalNode.Id == Terminals.Identifier)
				{
					sqlModelColumn.IsDirectReference = true;

					if (aliasedExpression.TerminalCount == 1)
					{
						sqlModelColumn.AliasNode = aliasedExpression.FirstTerminalNode;
					}
				}

				sqlModelColumn.ObjectReferences.AddRange(objectReferences);
				ResolveSqlModelReferences(sqlModelColumn, GetIdentifiers(aliasedExpression).ToArray());
				measureColumns.Add(sqlModelColumn);
			}

			return measureColumns;
		}

		private static IEnumerable<StatementGrammarNode> GetIdentifiers(StatementGrammarNode nonTerminal, params string[] nodeIds)
		{
			if (nodeIds.Length == 0)
			{
				nodeIds = StandardIdentifierIds;
			}

			return nonTerminal.GetDescendantsWithinSameQuery(nodeIds);
		}

		private void ResolveInlineViewOrCommonTableExpressionRelations()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				ResolveConcatenatedQueryBlocks(queryBlock);

				ResolveCrossAndOuterAppliedTableReferences(queryBlock);

				ResolveParentCorrelatedQueryBlock(queryBlock);

				if (queryBlock.Type == QueryBlockType.CommonTableExpression && queryBlock.PrecedingConcatenatedQueryBlock == null)
				{
					_unreferencedQueryBlocks.Add(queryBlock);
				}
			}

			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				foreach (var nestedQueryReference in queryBlock.ObjectReferences.Where(t => t.Type != ReferenceType.SchemaObject))
				{
					if (nestedQueryReference.Type == ReferenceType.InlineView)
					{
						nestedQueryReference.QueryBlocks.Add(_queryBlockNodes[nestedQueryReference.ObjectNode]);
					}
					else if (_objectReferenceCteRootNodes.ContainsKey(nestedQueryReference))
					{
						var commonTableExpressionNode = _objectReferenceCteRootNodes[nestedQueryReference];
						var nestedTableFullyQualifiedName = OracleObjectIdentifier.Create(null, nestedQueryReference.ObjectNode.Token.Value);
						foreach (var referencedQueryBlock in commonTableExpressionNode
							.Where(nodeName => OracleObjectIdentifier.Create(null, nodeName.Value) == nestedTableFullyQualifiedName))
						{
							var cteQueryBlockNode = referencedQueryBlock.Key.GetDescendants(NonTerminals.QueryBlock).FirstOrDefault();
							if (cteQueryBlockNode != null)
							{
								var referredQueryBlock = _queryBlockNodes[cteQueryBlockNode];
								nestedQueryReference.QueryBlocks.Add(referredQueryBlock);

								_unreferencedQueryBlocks.Remove(referredQueryBlock);
							}
						}
					}
				}

				foreach (var accessibleQueryBlock in _accessibleQueryBlockRoot[queryBlock])
				{
					var accesibleQueryBlockRoot = accessibleQueryBlock.GetDescendants(NonTerminals.QueryBlock).FirstOrDefault();
					if (accesibleQueryBlockRoot != null)
					{
						queryBlock.AccessibleQueryBlocks.Add(_queryBlockNodes[accesibleQueryBlockRoot]);
					}
				}
			}
		}

		private void ApplyExplicitCommonTableExpressionColumnNames()
		{
			foreach (var queryBlocks in _commonTableExpressionExplicitColumnNames.Keys.ToArray())
			{
				ApplyExplicitCommonTableExpressionColumnNames(queryBlocks);
			}
		}

		private void ApplyExplicitCommonTableExpressionColumnNames(OracleQueryBlock queryBlock)
		{
			IList<string> columnNames;
			if (queryBlock.Type != QueryBlockType.CommonTableExpression ||
				!_commonTableExpressionExplicitColumnNames.TryGetValue(queryBlock, out columnNames))
			{
				return;
			}

			var columnIndex = 0;
			foreach (var column in queryBlock.Columns.Where(c => !c.IsAsterisk))
			{
				if (columnNames.Count == columnIndex)
				{
					break;
				}

				column.ExplicitNormalizedName = columnNames[columnIndex++];
			}

			_commonTableExpressionExplicitColumnNames.Remove(queryBlock);
		}

		private void ResolveRedundantTerminals()
		{
			ResolveRedundantCommonTableExpressions();

			ResolveRedundantSelectListColumns();
			
			ResolveRedundantQualifiers();
		}

		private void ResolveRedundantCommonTableExpressions()
		{
			foreach (var queryBlockDependentQueryBlocks in _unreferencedQueryBlocks)
			{
				queryBlockDependentQueryBlocks.IsRedundant = true;

				var commonTableExpression = queryBlockDependentQueryBlocks.RootNode.GetAncestor(NonTerminals.CommonTableExpression);
				var redundantTerminals = new List<StatementGrammarNode>();
				var precedingTerminal = commonTableExpression.PrecedingTerminal;
				var followingTerminal = commonTableExpression.FollowingTerminal;
				if (followingTerminal != null && String.Equals(followingTerminal.Id, Terminals.Comma))
				{
					redundantTerminals.AddRange(commonTableExpression.Terminals);
					redundantTerminals.Add(followingTerminal);
				}
				else if (String.Equals(precedingTerminal.Id, Terminals.Comma) || String.Equals(precedingTerminal.Id, Terminals.With))
				{
					redundantTerminals.Add(precedingTerminal);
					redundantTerminals.AddRange(commonTableExpression.Terminals);
				}

				var terminalGroup = new RedundantTerminalGroup(redundantTerminals, RedundancyType.UnusedQueryBlock);
				_redundantTerminalGroups.Add(terminalGroup);
			}
		}

		private void ResolveRedundantSelectListColumns()
		{
			foreach (var queryBlock in _queryBlockNodes.Values.Where(qb => qb != MainQueryBlock && !qb.HasDistinctResultSet))
			{
				var redundantColumns = 0;
				var explicitSelectListColumns = queryBlock.Columns.Where(c => c.HasExplicitDefinition).ToArray();
				foreach (var column in explicitSelectListColumns.Where(c => !c.IsReferenced))
				{
					if (++redundantColumns == explicitSelectListColumns.Length - queryBlock.AttachedColumns.Count)
					{
						break;
					}

					var initialPrecedingQueryBlock = queryBlock.AllPrecedingConcatenatedQueryBlocks.LastOrDefault();
					if (initialPrecedingQueryBlock != null)
					{
						var initialQueryBlockColumn = initialPrecedingQueryBlock.Columns
							.Where(c => c.HasExplicitDefinition)
							.Skip(redundantColumns - 1)
							.FirstOrDefault();

						if (initialQueryBlockColumn != null && (initialQueryBlockColumn.IsReferenced || initialPrecedingQueryBlock == MainQueryBlock))
						{
							continue;
						}
					}

					var terminalGroup = new List<StatementGrammarNode>(column.RootNode.Terminals);
					_redundantTerminals.AddRange(terminalGroup);

					StatementGrammarNode commaTerminal;
					if (!TryMakeRedundantIfComma(column.RootNode.PrecedingTerminal, out commaTerminal))
					{
						if (TryMakeRedundantIfComma(column.RootNode.FollowingTerminal, out commaTerminal))
						{
							terminalGroup.Add(commaTerminal);
						}
					}
					else
					{
						terminalGroup.Insert(0, commaTerminal);
					}

					_redundantTerminalGroups.Add(new RedundantTerminalGroup(terminalGroup, RedundancyType.UnusedColumn));
				}
			}
		}

		private bool TryMakeRedundantIfComma(StatementGrammarNode terminal, out StatementGrammarNode commaTerminal)
		{
			commaTerminal = terminal != null && terminal.Id == Terminals.Comma && _redundantTerminals.Add(terminal) ? terminal : null;
			return commaTerminal != null;
		}

		private void ResolveRedundantQualifiers()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				var ownerNameObjectReferences = queryBlock.ObjectReferences
					.Where(o => o.OwnerNode != null && o.SchemaObject != null)
					.ToLookup(o => o.SchemaObject.Name);

				var removedObjectReferenceOwners = new HashSet<OracleDataObjectReference>();
				if (!IsSimpleModel)
				{
					foreach (var ownerNameObjectReference in ownerNameObjectReferences)
					{
						var uniqueObjectReferenceCount = ownerNameObjectReference.Count();
						if (uniqueObjectReferenceCount != 1)
						{
							continue;
						}

						foreach (var objectReference in ownerNameObjectReference.Where(o => o.SchemaObject.Owner == DatabaseModel.CurrentSchema.ToQuotedIdentifier()))
						{
							var terminals = objectReference.RootNode.Terminals.TakeWhile(t => t != objectReference.ObjectNode);
							CreateRedundantTerminalGroup(terminals);
							removedObjectReferenceOwners.Add(objectReference);
						}
					}
				}

				var otherRedundantOwnerReferences = ((IEnumerable<OracleReference>)queryBlock.AllProgramReferences).Concat(queryBlock.AllTypeReferences).Concat(queryBlock.AllSequenceReferences)
					.Where(o => o.OwnerNode != null && IsSchemaObjectInCurrentSchemaOrAccessibleByPublicSynonym(o.SchemaObject));
				foreach (var reference in otherRedundantOwnerReferences)
				{
					CreateRedundantTerminalGroup(new[] { reference.OwnerNode, reference.OwnerNode.FollowingTerminal });
				}

				foreach (var columnReference in queryBlock.AllColumnReferences.Where(c => c.ObjectNode != null && c.RootNode != null))
				{
					var uniqueObjectReferenceCount = queryBlock.ObjectReferences.Where(o => o.Columns.Any(c => c.Name == columnReference.NormalizedName)).Distinct().Count();
					if (uniqueObjectReferenceCount != 1)
					{
						if (columnReference.OwnerNode != null && removedObjectReferenceOwners.Contains(columnReference.ValidObjectReference))
						{
							var redundantSchemaPrefixTerminals = columnReference.RootNode.Terminals.TakeWhile(t => t != columnReference.ObjectNode);
							CreateRedundantTerminalGroup(redundantSchemaPrefixTerminals);
						}

						continue;
					}

					var requiredNode = columnReference.IsCorrelated ? columnReference.ObjectNode : columnReference.ColumnNode;
					var terminals = columnReference.RootNode.Terminals.TakeWhile(t => t != requiredNode);
					CreateRedundantTerminalGroup(terminals);
				}
			}
		}

		private void CreateRedundantTerminalGroup(IEnumerable<StatementGrammarNode> terminals)
		{
			var terminalGroup = new RedundantTerminalGroup(terminals, RedundancyType.Qualifier);
			if (terminalGroup.All(t => _redundantTerminals.Add(t)))
			{
				_redundantTerminalGroups.Add(terminalGroup);	
			}
		}

		private bool IsSchemaObjectInCurrentSchemaOrAccessibleByPublicSynonym(OracleSchemaObject schemaObject)
		{
			if (schemaObject == null)
			{
				return false;
			}

			var isSchemaObjectInCurrentSchema = schemaObject.Owner == DatabaseModel.CurrentSchema.ToQuotedIdentifier();
			var isAccessibleByPublicSynonym = schemaObject.Synonyms.Any(s => s.Owner == OracleDatabaseModelBase.SchemaPublic && s.Name == schemaObject.Name);
			return isSchemaObjectInCurrentSchema || isAccessibleByPublicSynonym;
		}

		private void BuildDmlModel()
		{
			ResolveMainObjectReferenceInsert();
			ResolveMainObjectReferenceUpdateOrDelete();

			var rootNode = Statement.RootNode.GetDescendantByIndex(0, 0);
			if (rootNode == null)
			{
				return;
			}

			var whereClauseRootNode = rootNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.WhereClause);
			if (whereClauseRootNode != null)
			{
				var whereClauseIdentifiers = whereClauseRootNode.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
				ResolveColumnAndFunctionReferenceFromIdentifiers(null, MainObjectReferenceContainer, whereClauseIdentifiers, StatementPlacement.Where, null);
			}

			if (MainObjectReferenceContainer.MainObjectReference != null)
			{
				ResolveFunctionReferences(MainObjectReferenceContainer.ProgramReferences);
				ResolveColumnObjectReferences(MainObjectReferenceContainer.ColumnReferences, new [] { MainObjectReferenceContainer.MainObjectReference }, new OracleDataObjectReference[0]);
			}
		}

		private void ResolveMainObjectReferenceUpdateOrDelete()
		{
			var rootNode = Statement.RootNode.GetDescendantByIndex(0, 0);
			if (rootNode == null)
			{
				return;
			}

			var tableReferenceNode = rootNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.TableReference);
			if (tableReferenceNode == null)
			{
				return;
			}

			var innerTableReference = tableReferenceNode.GetDescendantsWithinSameQuery(NonTerminals.InnerTableReference).SingleOrDefault();
			if (innerTableReference == null)
			{
				return;
			}

			var objectIdentifier = innerTableReference.GetDescendantByPath(NonTerminals.QueryTableExpression, Terminals.ObjectIdentifier);
			if (objectIdentifier != null)
			{
				var objectReferenceAlias = innerTableReference.ParentNode.ChildNodes.SingleOrDefault(n => n.Id == Terminals.ObjectAlias);
				MainObjectReferenceContainer.MainObjectReference = CreateDataObjectReference(tableReferenceNode, objectIdentifier, objectReferenceAlias);
			}
			else if (MainQueryBlock != null)
			{
				MainObjectReferenceContainer.MainObjectReference = MainQueryBlock.SelfObjectReference;
			}

			if (rootNode.FirstTerminalNode.Id != Terminals.Update)
			{
				return;
			}

			var updateListNode = rootNode.GetDescendantByPath(NonTerminals.UpdateSetClause, NonTerminals.UpdateSetColumnsOrObjectValue);
			if (updateListNode == null)
			{
				return;
			}

			var identifiers = updateListNode.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.Level);
			ResolveColumnAndFunctionReferenceFromIdentifiers(null, MainObjectReferenceContainer, identifiers, StatementPlacement.None, null, GetPrefixNonTerminalFromPrefixedUpdatedColumnReference);
		}

		private StatementGrammarNode GetPrefixNonTerminalFromPrefixedUpdatedColumnReference(StatementGrammarNode identifier)
		{
			return identifier.ParentNode.Id == NonTerminals.PrefixedUpdatedColumnReference
				? identifier.ParentNode.GetDescendantByPath(NonTerminals.Prefix)
				: GetPrefixNodeFromPrefixedColumnReference(identifier);
		}

		private void ResolveMainObjectReferenceInsert()
		{
			var insertIntoClauses = Statement.RootNode.GetDescendantsWithinSameQuery(NonTerminals.InsertIntoClause);
			foreach (var insertIntoClause in insertIntoClauses)
			{
				var dmlTableExpressionClause = insertIntoClause.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.DmlTableExpressionClause);
				StatementGrammarNode objectReferenceAlias = null;
				StatementGrammarNode objectIdentifier = null;
				if (dmlTableExpressionClause != null)
				{
					objectReferenceAlias = dmlTableExpressionClause.ChildNodes.SingleOrDefault(n => n.Id == Terminals.ObjectAlias);
					objectIdentifier = dmlTableExpressionClause.GetDescendantByPath(NonTerminals.QueryTableExpression, Terminals.ObjectIdentifier);
				}

				if (objectIdentifier == null)
				{
					continue;
				}

				var dataObjectReference = CreateDataObjectReference(dmlTableExpressionClause, objectIdentifier, objectReferenceAlias);

				var referenceContainer = new OracleMainObjectReferenceContainer(this);
				var insertTarget = new OracleInsertTarget(this)
				{
					TargetNode = dmlTableExpressionClause,
					RootNode = insertIntoClause.ParentNode
				};
				
				insertTarget.ObjectReferences.Add(dataObjectReference);
				insertTarget.ColumnListNode = insertIntoClause.GetDescendantByPath(NonTerminals.ParenthesisEnclosedIdentifierList);
				if (insertTarget.ColumnListNode != null)
				{
					var columnIdentiferNodes = insertTarget.ColumnListNode.GetDescendants(Terminals.Identifier, Terminals.Level);
					ResolveColumnAndFunctionReferenceFromIdentifiers(null, referenceContainer, columnIdentiferNodes, StatementPlacement.None, null);
					ResolveColumnObjectReferences(referenceContainer.ColumnReferences, insertTarget.ObjectReferences, new OracleDataObjectReference[0]);
				}

				insertTarget.ValueList = insertIntoClause.ParentNode.GetDescendantByPath(NonTerminals.InsertValuesClause, NonTerminals.ParenthesisEnclosedExpressionOrOrDefaultValueList)
				                         ?? insertIntoClause.ParentNode.GetDescendantByPath(NonTerminals.InsertValuesOrSubquery, NonTerminals.InsertValuesClause, NonTerminals.ParenthesisEnclosedExpressionOrOrDefaultValueList);

				if (insertTarget.ValueList == null)
				{
					var nestedQuery = insertIntoClause.ParentNode.GetDescendantByPath(NonTerminals.InsertValuesOrSubquery, NonTerminals.NestedQuery);
					if (nestedQuery != null)
					{
						var queryBlock = nestedQuery.GetDescendantsWithinSameQuery(NonTerminals.QueryBlock).FirstOrDefault();
						if (queryBlock != null)
						{
							insertTarget.RowSource = _queryBlockNodes[queryBlock];
						}
					}
				}
				else
				{
					var identifiers = insertTarget.ValueList.GetDescendantsWithinSameQuery(Terminals.Identifier);
					ResolveColumnAndFunctionReferenceFromIdentifiers(null, insertTarget, identifiers, StatementPlacement.ValuesClause, null); // TODO: Fix root node is not set
					ResolveColumnObjectReferences(insertTarget.ColumnReferences, insertTarget.ObjectReferences, new OracleDataObjectReference[0]);
					ResolveFunctionReferences(insertTarget.ProgramReferences);
				}

				insertTarget.ColumnReferences.AddRange(referenceContainer.ColumnReferences);

				_insertTargets.Add(insertTarget);
			}
		}

		private OracleDataObjectReference CreateDataObjectReference(StatementGrammarNode rootNode, StatementGrammarNode objectIdentifier, StatementGrammarNode aliasNode)
		{
			var queryTableExpressionNode = objectIdentifier.ParentNode;
			var schemaPrefixNode = queryTableExpressionNode.GetDescendantByPath(NonTerminals.SchemaPrefix, Terminals.SchemaIdentifier);

			OracleSchemaObject schemaObject = null;
			if (!IsSimpleModel)
			{
				var owner = schemaPrefixNode == null ? null : schemaPrefixNode.Token.Value;
				schemaObject = _databaseModel.GetFirstSchemaObject<OracleDataObject>(_databaseModel.GetPotentialSchemaObjectIdentifiers(owner, objectIdentifier.Token.Value));
			}

			return
				new OracleDataObjectReference(ReferenceType.SchemaObject)
				{
					RootNode = rootNode,
					OwnerNode = schemaPrefixNode,
					ObjectNode = objectIdentifier,
					DatabaseLinkNode = GetDatabaseLinkFromQueryTableExpression(queryTableExpressionNode),
					AliasNode = aliasNode,
					SchemaObject = schemaObject
				};
		}

		private void ResolveCrossAndOuterAppliedTableReferences(OracleQueryBlock queryBlock)
		{
			var tableReferenceJoinClause = queryBlock.RootNode.GetPathFilterAncestor(n => n.Id != NonTerminals.QueryBlock, NonTerminals.TableReferenceJoinClause);
			var isApplied = queryBlock.RootNode.GetPathFilterAncestor(n => n.Id != NonTerminals.QueryBlock, NonTerminals.CrossOrOuterApplyClause) != null;
			if (tableReferenceJoinClause == null || !isApplied)
			{
				return;
			}

			var appliedTableReferenceNode = tableReferenceJoinClause[0];
			if (appliedTableReferenceNode == null)
			{
				return;
			}

			var parentCorrelatedQueryBlock = GetQueryBlock(tableReferenceJoinClause);
			queryBlock.CrossOrOuterApplyReference = parentCorrelatedQueryBlock.ObjectReferences.SingleOrDefault(o => o.RootNode == appliedTableReferenceNode);
		}

		private void ResolveParentCorrelatedQueryBlock(OracleQueryBlock queryBlock, bool allowMoreThanOneLevel = false)
		{
			var nestedQueryRoot = queryBlock.RootNode.ParentNode.ParentNode;
			
			foreach (var parentId in new[] { NonTerminals.Expression, NonTerminals.Condition })
			{
				var parentExpression = nestedQueryRoot.GetPathFilterAncestor(n => allowMoreThanOneLevel || n.Id != NonTerminals.NestedQuery, parentId);
				if (parentExpression == null)
					continue;
				
				queryBlock.ParentCorrelatedQueryBlock = GetQueryBlock(parentExpression);
				foreach (var asteriskColumn in queryBlock.AsteriskColumns)
				{
					asteriskColumn.RegisterOuterReference();
				}

				return;
			}
		}

		private void ResolveConcatenatedQueryBlocks(OracleQueryBlock queryBlock)
		{
			if (queryBlock.RootNode.ParentNode.ParentNode.Id != NonTerminals.ConcatenatedSubquery)
				return;
			
			var grandGrandParent = queryBlock.RootNode.ParentNode.ParentNode.ParentNode;
			var parentQueryBlockNode = grandGrandParent.Id == NonTerminals.ConcatenatedSubquery
				? grandGrandParent.GetDescendantByPath(NonTerminals.Subquery, NonTerminals.QueryBlock)
				: grandGrandParent.GetDescendantByPath(NonTerminals.QueryBlock);

			if (parentQueryBlockNode == null)
				return;
			
			var precedingQueryBlock = _queryBlockNodes[parentQueryBlockNode];
			precedingQueryBlock.FollowingConcatenatedQueryBlock = queryBlock;
			queryBlock.PrecedingConcatenatedQueryBlock = precedingQueryBlock;

			var setOperation = queryBlock.RootNode.ParentNode.ParentNode[0];
			if (precedingQueryBlock.Type != QueryBlockType.CommonTableExpression || setOperation == null || setOperation.TerminalCount != 2 || setOperation[0].Id != Terminals.Union || setOperation[1].Id != Terminals.All)
			{
				return;
			}

			var anchorReferences = queryBlock.ObjectReferences
				.Where(r => r.Type == ReferenceType.SchemaObject && r.OwnerNode == null && r.DatabaseLinkNode == null && r.FullyQualifiedObjectName.NormalizedName == precedingQueryBlock.NormalizedAlias)
				.ToArray();
			if (anchorReferences.Length == 0)
			{
				return;
			}

			queryBlock.AliasNode = null;

			foreach(var anchorReference in anchorReferences)
			{
				queryBlock.ObjectReferences.Remove(anchorReference);

				var newAnchorReference =
					new OracleDataObjectReference(ReferenceType.CommonTableExpression)
					{
						Owner = queryBlock,
						RootNode = anchorReference.RootNode,
						ObjectNode = anchorReference.ObjectNode,
						AliasNode = anchorReference.AliasNode
					};

				newAnchorReference.QueryBlocks.Add(queryBlock);
				queryBlock.ObjectReferences.Add(newAnchorReference);
			}

			precedingQueryBlock.IsRecursive = true;
		}

		private void ResolveReferences()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				ResolveOrderByReferences(queryBlock);

				ResolveFunctionReferences(queryBlock.AllProgramReferences);

				var correlatedReferences = new List<OracleDataObjectReference>();
				if (queryBlock.ParentCorrelatedQueryBlock != null)
				{
					correlatedReferences.AddRange(queryBlock.ParentCorrelatedQueryBlock.ObjectReferences);
				}

				if (queryBlock.CrossOrOuterApplyReference != null)
				{
					correlatedReferences.Add(queryBlock.CrossOrOuterApplyReference);
				}

				var columnReferences = queryBlock.AllColumnReferences.Where(c => c.SelectListColumn == null || c.SelectListColumn.HasExplicitDefinition);
				ResolveColumnObjectReferences(columnReferences, queryBlock.ObjectReferences, correlatedReferences);

				ResolveDatabaseLinks(queryBlock);
			}
		}

		private void ResolveDatabaseLinks(OracleQueryBlock queryBlock)
		{
			if (IsSimpleModel)
			{
				return;
			}

			foreach (var databaseLinkReference in queryBlock.DatabaseLinkReferences)
			{
				var databaseLinkBuilder = new StringBuilder(128);
				var databaseLinkWithoutInstanceBuilder = new StringBuilder(128);
				var includesDomain = false;
				var hasInstanceDefinition = false;
				foreach (var terminal in databaseLinkReference.DatabaseLinkNode.Terminals)
				{
					var isLinkIdentifier = terminal.Id == Terminals.DatabaseLinkIdentifier;
					if (terminal.Id == Terminals.Dot || (isLinkIdentifier && terminal.Token.Value.Contains('.')))
					{
						includesDomain = true;
					}

					var characterIndex = 0;
					if (terminal.Id == Terminals.AtCharacter || (isLinkIdentifier && (characterIndex = terminal.Token.Value.IndexOf('@')) != -1))
					{
						hasInstanceDefinition = true;

						if (isLinkIdentifier)
						{
							databaseLinkWithoutInstanceBuilder.Append(terminal.Token.Value.Substring(0, characterIndex).Trim('"'));
						}
					}

					databaseLinkBuilder.Append(terminal.Token.Value.Trim('"'));

					if (!hasInstanceDefinition)
					{
						databaseLinkWithoutInstanceBuilder.Append(terminal.Token.Value);
					}
				}

				var potentialIdentifiers = _databaseModel.GetPotentialSchemaObjectIdentifiers(null, databaseLinkBuilder.ToString()).ToList();

				if (hasInstanceDefinition)
				{
					potentialIdentifiers.AddRange(_databaseModel.GetPotentialSchemaObjectIdentifiers(null, databaseLinkWithoutInstanceBuilder.ToString()));
				}

				if (!includesDomain && !String.IsNullOrEmpty(DatabaseModel.DatabaseDomainName))
				{
					databaseLinkWithoutInstanceBuilder.Append(".");
					databaseLinkWithoutInstanceBuilder.Append(DatabaseModel.DatabaseDomainName.ToUpperInvariant());
					potentialIdentifiers.AddRange(_databaseModel.GetPotentialSchemaObjectIdentifiers(null, databaseLinkWithoutInstanceBuilder.ToString()));
				}

				databaseLinkReference.DatabaseLink = _databaseModel.GetFirstDatabaseLink(potentialIdentifiers.ToArray());
			}
		}

		private void ExposeAsteriskColumns()
		{
			foreach (var asteriskTableReference in _asteriskTableReferences)
			{
				var asteriskColumn = asteriskTableReference.Key;
				var ownerQueryBlock = asteriskColumn.Owner;
				var columnIndex = ownerQueryBlock.IndexOf(asteriskColumn);

				foreach (var objectReference in asteriskTableReference.Value)
				{
					IEnumerable<OracleSelectListColumn> exposedColumns;
					switch (objectReference.Type)
					{
						case ReferenceType.SchemaObject:
							var dataObject = objectReference.SchemaObject.GetTargetSchemaObject() as OracleDataObject;
							if (dataObject == null)
								continue;

							exposedColumns = dataObject.Columns.Values
								.Select(c => new OracleSelectListColumn(this, asteriskColumn)
								{
									IsDirectReference = true,
									ColumnDescription = c
								});
							break;
						case ReferenceType.TableCollection:
						case ReferenceType.XmlTable:
						case ReferenceType.JsonTable:
						case ReferenceType.SqlModel:
							exposedColumns = objectReference.Columns
								.Select(c => new OracleSelectListColumn(this, asteriskColumn)
								{
									IsDirectReference = true,
									ColumnDescription = c
								});
							break;
						case ReferenceType.CommonTableExpression:
							foreach (var queryBlock in objectReference.QueryBlocks)
							{
								ApplyExplicitCommonTableExpressionColumnNames(queryBlock);
							}

							goto case ReferenceType.InlineView;
						case ReferenceType.InlineView:
							var columns = new List<OracleSelectListColumn>();
							foreach (var column in objectReference.QueryBlocks.SelectMany(qb => qb.Columns).Where(c => !c.IsAsterisk))
							{
								column.RegisterOuterReference();
								columns.Add(column.AsImplicit(asteriskColumn));
							}

							exposedColumns = columns;
							break;
						default:
							throw new NotImplementedException(String.Format("Reference '{0}' is not implemented yet. ", objectReference.Type));
					}

					var exposedColumnDictionary = new Dictionary<string, OracleColumnReference>();
					foreach (var exposedColumn in exposedColumns)
					{
						exposedColumn.Owner = ownerQueryBlock;

						OracleColumnReference columnReference;
						if (String.IsNullOrEmpty(exposedColumn.NormalizedName) || !exposedColumnDictionary.TryGetValue(exposedColumn.NormalizedName, out columnReference))
						{
							columnReference = CreateColumnReference(exposedColumn, exposedColumn.Owner, exposedColumn, StatementPlacement.SelectList, asteriskColumn.RootNode.LastTerminalNode, null);

							if (!String.IsNullOrEmpty(exposedColumn.NormalizedName))
							{
								exposedColumnDictionary.Add(exposedColumn.NormalizedName, columnReference);
							}

							columnReference.ColumnNodeObjectReferences.Add(objectReference);
						}

						columnReference.ColumnNodeColumnReferences.Add(exposedColumn.ColumnDescription);

						exposedColumn.ColumnReferences.Add(columnReference);

						ownerQueryBlock.AddSelectListColumn(exposedColumn, ++columnIndex);
					}
				}
			}
		}

		private void ResolveFunctionReferences(IEnumerable<OracleProgramReference> programReferences)
		{
			var programsTransferredToTypes = new List<OracleProgramReference>();
			foreach (var programReference in programReferences)
			{
				if (UpdateFunctionReferenceWithMetadata(programReference) != null)
					continue;

				var typeReference = ResolveTypeReference(programReference);
				if (typeReference == null)
					continue;

				programsTransferredToTypes.Add(programReference);
				programReference.Container.TypeReferences.Add(typeReference);
			}

			programsTransferredToTypes.ForEach(f => f.Container.ProgramReferences.Remove(f));
		}

		private OracleTypeReference ResolveTypeReference(OracleProgramReference functionReference)
		{
			if (functionReference.ParameterListNode == null || functionReference.OwnerNode != null)
				return null;

			var identifierCandidates = _databaseModel.GetPotentialSchemaObjectIdentifiers(functionReference.FullyQualifiedObjectName.NormalizedName, functionReference.NormalizedName);

			var schemaObject = _databaseModel.GetFirstSchemaObject<OracleTypeBase>(identifierCandidates);
			if (schemaObject == null)
				return null;

			return
				new OracleTypeReference
				{
					OwnerNode = functionReference.ObjectNode,
					DatabaseLinkNode = functionReference.DatabaseLinkNode,
					DatabaseLink = functionReference.DatabaseLink,
					Owner = functionReference.Owner,
					ParameterReferences = functionReference.ParameterReferences,
					ParameterListNode = functionReference.ParameterListNode,
					RootNode = functionReference.RootNode,
					SchemaObject = schemaObject,
					SelectListColumn = functionReference.SelectListColumn,
					ObjectNode = functionReference.FunctionIdentifierNode
				};
		}

		private OracleProgramMetadata UpdateFunctionReferenceWithMetadata(OracleProgramReference programReference)
		{
			if (IsSimpleModel || programReference.DatabaseLinkNode != null)
				return null;

			var owner = String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner)
				? _databaseModel.CurrentSchema
				: programReference.FullyQualifiedObjectName.NormalizedOwner;

			var originalIdentifier = OracleProgramIdentifier.CreateFromValues(owner, programReference.FullyQualifiedObjectName.NormalizedName, programReference.NormalizedName);
			var parameterCount = programReference.ParameterReferences == null ? 0 : programReference.ParameterReferences.Count;
			var hasAnalyticClause = programReference.AnalyticClauseNode != null;
			var result = _databaseModel.GetProgramMetadata(originalIdentifier, parameterCount, true, hasAnalyticClause);
			if (result.Metadata == null && !String.IsNullOrEmpty(originalIdentifier.Package) && String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner))
			{
				var identifier = OracleProgramIdentifier.CreateFromValues(originalIdentifier.Package, null, originalIdentifier.Name);
				result = _databaseModel.GetProgramMetadata(identifier, parameterCount, false, hasAnalyticClause);
			}

			if (result.Metadata == null && String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner))
			{
				var identifier = OracleProgramIdentifier.CreateFromValues(OracleDatabaseModelBase.SchemaPublic, originalIdentifier.Package, originalIdentifier.Name);
				result = _databaseModel.GetProgramMetadata(identifier, parameterCount, false, hasAnalyticClause);
			}

			if (result.Metadata != null && String.IsNullOrEmpty(result.Metadata.Identifier.Package) &&
				programReference.ObjectNode != null)
			{
				programReference.OwnerNode = programReference.ObjectNode;
				programReference.ObjectNode = null;
			}

			programReference.SchemaObject = result.SchemaObject;

			return programReference.Metadata = result.Metadata;
		}

		public OracleQueryBlock GetQueryBlock(int position)
		{
			return _queryBlockNodes.Values
				.Where(qb => qb.RootNode.SourcePosition.ContainsIndex(position))
				.OrderByDescending(qb => qb.RootNode.Level)
				.FirstOrDefault();
		}

		public OracleColumnReference GetColumnReference(StatementGrammarNode columnIdentifer)
		{
			return AllReferenceContainers.SelectMany(c => c.ColumnReferences).FilterRecursiveReferences().SingleOrDefault(c => c.ColumnNode == columnIdentifer);
		}

		public OracleProgramReference GetProgramReference(StatementGrammarNode identifer)
		{
			return AllReferenceContainers.SelectMany(c => c.ProgramReferences).FilterRecursiveReferences().SingleOrDefault(c => c.FunctionIdentifierNode == identifer);
		}

		public OracleTypeReference GetTypeReference(StatementGrammarNode typeIdentifer)
		{
			return AllReferenceContainers.SelectMany(c => c.TypeReferences).FilterRecursiveReferences().SingleOrDefault(c => c.ObjectNode == typeIdentifer);
		}

		public OracleSequenceReference GetSequenceReference(StatementGrammarNode sequenceIdentifer)
		{
			return AllReferenceContainers.SelectMany(c => c.SequenceReferences).FilterRecursiveReferences().SingleOrDefault(c => c.ObjectNode == sequenceIdentifer);
		}

		public T GetReference<T>(StatementGrammarNode objectIdentifer) where T : OracleReference
		{
			return AllReferenceContainers.SelectMany(c => c.AllReferences).OfType<T>().FirstOrDefault(c => c.ObjectNode == objectIdentifer);
		}

		public OracleQueryBlock GetQueryBlock(StatementGrammarNode node)
		{
			var queryBlockNode = node.GetAncestor(NonTerminals.QueryBlock);
			if (queryBlockNode == null)
			{
				var orderByClauseNode = node.GetPathFilterAncestor(n => n.Id != NonTerminals.NestedQuery, NonTerminals.OrderByClause, true);
				if (orderByClauseNode == null)
					return null;
				
				var queryBlock = _queryBlockNodes.Values.SingleOrDefault(qb => qb.OrderByClause == orderByClauseNode);
				if (queryBlock == null)
					return null;

				queryBlockNode = queryBlock.RootNode;
			}

			return queryBlockNode == null ? null : _queryBlockNodes[queryBlockNode];
		}

		private void ResolveColumnObjectReferences(IEnumerable<OracleColumnReference> columnReferences, ICollection<OracleDataObjectReference> accessibleRowSourceReferences, ICollection<OracleDataObjectReference> parentCorrelatedRowSourceReferences)
		{
			foreach (var columnReference in columnReferences.ToArray())
			{
				if (columnReference.Placement == StatementPlacement.OrderBy)
				{
					if (columnReference.Owner.FollowingConcatenatedQueryBlock != null)
					{
						ResolveConcatenatedQueryBlockOrderByReferences(columnReference);

						continue;
					}

					if (columnReference.ObjectNode == null)
					{
						var orderByColumnAliasOrAsteriskReferences = columnReference.Owner.Columns
							.Where(c => c.NormalizedName == columnReference.NormalizedName)
							.Select(c => c.ColumnDescription);

						columnReference.ColumnNodeColumnReferences.AddRange(orderByColumnAliasOrAsteriskReferences);
					}
				}

				IEnumerable<OracleDataObjectReference> effectiveAccessibleRowSourceReferences = accessibleRowSourceReferences;
				if (columnReference.Placement == StatementPlacement.Join || columnReference.Placement == StatementPlacement.TableReference)
				{
					effectiveAccessibleRowSourceReferences = effectiveAccessibleRowSourceReferences
								.Where(r => r.RootNode.SourcePosition.IndexEnd < columnReference.RootNode.SourcePosition.IndexStart);

					if (columnReference.Placement == StatementPlacement.Join)
					{
						var effectiveFromClause = columnReference.RootNode.GetAncestor(NonTerminals.FromClause);
						effectiveAccessibleRowSourceReferences = effectiveAccessibleRowSourceReferences
							.Where(r => r.RootNode.GetAncestor(NonTerminals.FromClause) == effectiveFromClause);
					}
				}

				if (columnReference.Placement == StatementPlacement.RecursiveSearchOrCycleClause)
				{
					var matchedColumns = columnReference.Owner.Columns.Where(c => !c.IsAsterisk && c.NormalizedName == columnReference.NormalizedName && !columnReference.Owner.AttachedColumns.Contains(c))
						.Join(columnReference.Owner.SelfObjectReference.Columns, c => c.NormalizedName, c => c.Name, (sc, c) => c);
					
					columnReference.ColumnNodeColumnReferences.AddRange(matchedColumns);
				}
				else if (columnReference.Placement != StatementPlacement.ValuesClause)
				{
					ResolveColumnReference(effectiveAccessibleRowSourceReferences, columnReference, false);
					if (columnReference.ColumnNodeObjectReferences.Count == 0)
					{
						ResolveColumnReference(parentCorrelatedRowSourceReferences, columnReference, true);
					}
				}

				var referencesSelectListColumn = (columnReference.Placement == StatementPlacement.OrderBy || columnReference.Placement == StatementPlacement.RecursiveSearchOrCycleClause) &&
				                                 columnReference.ColumnNodeObjectReferences.Count == 0 &&
				                                 columnReference.OwnerNode == null && columnReference.ColumnNodeColumnReferences.Count > 0;
				if (referencesSelectListColumn)
				{
					columnReference.ColumnNodeObjectReferences.Add(columnReference.Owner.SelfObjectReference);
				}

				var columnDescription = columnReference.ColumnNodeColumnReferences.FirstOrDefault();

				if (columnDescription != null &&
				    columnReference.ColumnNodeObjectReferences.Count == 1)
				{
					columnReference.ColumnDescription = columnDescription;
				}

				TryColumnReferenceAsProgramOrSequenceReference(columnReference);
			}
		}

		private void ResolveColumnReference(IEnumerable<OracleDataObjectReference> rowSources, OracleColumnReference columnReference, bool correlatedRowSources)
		{
			var hasColumnReferencesToSelectList = columnReference.Placement == StatementPlacement.OrderBy && columnReference.ColumnNodeColumnReferences.Count > 0;

			foreach (var rowSourceReference in rowSources)
			{
				if (columnReference.ObjectNode != null &&
				    (rowSourceReference.FullyQualifiedObjectName == columnReference.FullyQualifiedObjectName ||
				     (columnReference.OwnerNode == null &&
				      rowSourceReference.Type == ReferenceType.SchemaObject && String.Equals(rowSourceReference.FullyQualifiedObjectName.NormalizedName, columnReference.FullyQualifiedObjectName.NormalizedName))))
				{
					columnReference.ObjectNodeObjectReferences.Add(rowSourceReference);
					columnReference.IsCorrelated = correlatedRowSources;
				}

				AddColumnNodeColumnReferences(rowSourceReference, columnReference, hasColumnReferencesToSelectList);
			}
		}

		private void AddColumnNodeColumnReferences(OracleDataObjectReference rowSourceReference, OracleColumnReference columnReference, bool hasColumnReferencesToSelectList)
		{
			if (!String.IsNullOrEmpty(columnReference.FullyQualifiedObjectName.NormalizedName) &&
				columnReference.ObjectNodeObjectReferences.Count == 0)
			{
				return;
			}

			var newColumnReferences = new List<OracleColumn>();
			switch (rowSourceReference.Type)
			{
				case ReferenceType.JsonTable:
				case ReferenceType.XmlTable:
				case ReferenceType.SqlModel:
					newColumnReferences.AddRange(GetColumnReferenceMatchingColumns(rowSourceReference, columnReference));
					break;
				case ReferenceType.SchemaObject:
				case ReferenceType.TableCollection:
					if (rowSourceReference.SchemaObject == null)
						return;

					if (columnReference.ColumnNode.Id == Terminals.RowIdPseudoColumn)
					{
						var oracleTable = rowSourceReference.SchemaObject.GetTargetSchemaObject() as OracleTable;
						if (oracleTable != null && oracleTable.RowIdPseudoColumn != null &&
						    (columnReference.ObjectNode == null || IsTableReferenceValid(columnReference, rowSourceReference)))
						{
							newColumnReferences.Add(oracleTable.RowIdPseudoColumn);
						}
					}
					else
					{
						newColumnReferences.AddRange(GetColumnReferenceMatchingColumns(rowSourceReference, columnReference));
					}

					break;
				case ReferenceType.InlineView:
				case ReferenceType.CommonTableExpression:
					if (columnReference.ObjectNode != null && !String.Equals(columnReference.FullyQualifiedObjectName.NormalizedName, rowSourceReference.FullyQualifiedObjectName.NormalizedName))
					{
						break;
					}

					var selectListColumns = rowSourceReference.QueryBlocks.SelectMany(qb => qb.NamedColumns[columnReference.NormalizedName]);
					foreach (var selectListColumn in selectListColumns)
					{
						newColumnReferences.Add(selectListColumn.ColumnDescription);
						selectListColumn.RegisterOuterReference();
					}
					
					break;
			}

			if (newColumnReferences.Count == 0)
			{
				return;
			}
			
			if (!hasColumnReferencesToSelectList)
			{
				columnReference.ColumnNodeColumnReferences.AddRange(newColumnReferences);
			}

			columnReference.ColumnNodeObjectReferences.Add(rowSourceReference);
		}

		private IEnumerable<OracleColumn> GetColumnReferenceMatchingColumns(OracleDataObjectReference rowSourceReference, OracleColumnReference columnReference)
		{
			return columnReference.ObjectNode == null || IsTableReferenceValid(columnReference, rowSourceReference)
				? rowSourceReference.Columns.Where(c => c.Name == columnReference.NormalizedName)
				: Enumerable.Empty<OracleColumn>();
		}

		private void TryColumnReferenceAsProgramOrSequenceReference(OracleColumnReference columnReference)
		{
			if (columnReference.ColumnNodeColumnReferences.Count != 0 || columnReference.ReferencesAllColumns || columnReference.Container == null)
				return;
			
			var programReference =
				new OracleProgramReference
				{
					FunctionIdentifierNode = columnReference.ColumnNode,
					DatabaseLinkNode = GetDatabaseLinkFromIdentifier(columnReference.ColumnNode),
					ObjectNode = columnReference.ObjectNode,
					OwnerNode = columnReference.OwnerNode,
					RootNode = columnReference.RootNode,
					Owner = columnReference.Owner,
					SelectListColumn = columnReference.SelectListColumn,
					Placement = columnReference.Placement,
					AnalyticClauseNode = null,
					ParameterListNode = null,
					ParameterReferences = null
				};

			UpdateFunctionReferenceWithMetadata(programReference);
			if (programReference.SchemaObject != null)
			{
				columnReference.Container.ProgramReferences.Add(programReference);
				columnReference.Container.ColumnReferences.Remove(columnReference);
			}
			else
			{
				ResolveSequenceReference(columnReference);
			}
		}

		private static void ResolveConcatenatedQueryBlockOrderByReferences(OracleColumnReference columnReference)
		{
			if (columnReference.ObjectNode != null)
			{
				return;
			}

			var isRecognized = true;
			var maximumReferences = new OracleColumn[0];
			var concatenatedQueryBlocks = new List<OracleQueryBlock> { columnReference.Owner };
			concatenatedQueryBlocks.AddRange(columnReference.Owner.AllFollowingConcatenatedQueryBlocks);
			for (var i = 0; i < concatenatedQueryBlocks.Count; i++)
			{
				var queryBlockColumnAliasReferences = concatenatedQueryBlocks[i].Columns
					.Where(c => c.NormalizedName == columnReference.NormalizedName)
					.Select(c => c.ColumnDescription)
					.ToArray();

				isRecognized &= queryBlockColumnAliasReferences.Length > 0 || i == concatenatedQueryBlocks.Count - 1;

				if (queryBlockColumnAliasReferences.Length > maximumReferences.Length)
				{
					maximumReferences = queryBlockColumnAliasReferences;
				}
			}

			if (isRecognized)
			{
				columnReference.ColumnNodeObjectReferences.Add(columnReference.Owner.SelfObjectReference);
				columnReference.ColumnNodeColumnReferences.AddRange(maximumReferences);
			}
		}

		private void ResolveSequenceReference(OracleColumnReference columnReference)
		{
			if (columnReference.ObjectNode == null ||
				columnReference.ObjectNodeObjectReferences.Count > 0)
				return;

			var identifierCandidates = _databaseModel.GetPotentialSchemaObjectIdentifiers(columnReference.FullyQualifiedObjectName);	
			var schemaObject = _databaseModel.GetFirstSchemaObject<OracleSequence>(identifierCandidates);
			if (schemaObject == null)
				return;
			
			var sequenceReference =
				new OracleSequenceReference
				{
					ObjectNode = columnReference.ObjectNode,
					DatabaseLinkNode = GetDatabaseLinkFromIdentifier(columnReference.ColumnNode),
					Owner = columnReference.Owner,
					OwnerNode = columnReference.OwnerNode,
					RootNode = columnReference.RootNode,
					SelectListColumn = columnReference.SelectListColumn,
					Placement = columnReference.Placement,
					SchemaObject = schemaObject
				};

			var sequence = (OracleSequence)schemaObject.GetTargetSchemaObject();
			columnReference.Container.SequenceReferences.Add(sequenceReference);
			var pseudoColumn = sequence.Columns.SingleOrDefault(c => c.Name == columnReference.NormalizedName);
			if (pseudoColumn != null)
			{
				columnReference.ColumnNodeObjectReferences.Add(sequenceReference);
				columnReference.ColumnNodeColumnReferences.Add(pseudoColumn);
				columnReference.ColumnDescription = pseudoColumn;
			}

			if (columnReference.ObjectNode != null)
			{
				columnReference.ObjectNodeObjectReferences.Add(sequenceReference);
			}
		}
		
		private bool IsTableReferenceValid(OracleColumnReference column, OracleDataObjectReference schemaObject)
		{
			var objectName = column.FullyQualifiedObjectName;
			return (String.IsNullOrEmpty(objectName.NormalizedName) || objectName.NormalizedName == schemaObject.FullyQualifiedObjectName.NormalizedName) &&
			       (String.IsNullOrEmpty(objectName.NormalizedOwner) || objectName.NormalizedOwner == schemaObject.FullyQualifiedObjectName.NormalizedOwner);
		}

		private void FindJoinColumnReferences(OracleQueryBlock queryBlock)
		{
			var fromClauses = GetAllChainedClausesByPath(queryBlock.RootNode.GetDescendantByPath(NonTerminals.FromClause), null, NonTerminals.FromClauseChained, NonTerminals.FromClause);
			foreach (var fromClause in fromClauses)
			{
				var joinClauses = fromClause.GetPathFilterDescendants(n => n.Id != NonTerminals.NestedQuery && n.Id != NonTerminals.FromClause, NonTerminals.JoinClause);
				foreach (var joinClause in joinClauses)
				{
					var joinCondition = joinClause.GetPathFilterDescendants(n => n.Id != NonTerminals.JoinClause, NonTerminals.JoinColumnsOrCondition).SingleOrDefault();
					if (joinCondition == null)
						continue;

					var identifiers = joinCondition.GetDescendants(Terminals.Identifier);
					ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, StatementPlacement.Join, null);

					var joinCondifitionClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(joinCondition);
					CreateGrammarSpecificFunctionReferences(joinCondifitionClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.Join, null);
				}
			}
		}

		private IEnumerable<StatementGrammarNode> GetAllChainedClausesByPath(StatementGrammarNode initialialSearchedClause, Func<StatementGrammarNode, StatementGrammarNode> getChainedRootFunction, params string[] chainNonTerminalIds)
		{
			while (initialialSearchedClause != null)
			{
				yield return initialialSearchedClause;

				if (getChainedRootFunction != null)
				{
					initialialSearchedClause = getChainedRootFunction(initialialSearchedClause);
				}

				initialialSearchedClause = initialialSearchedClause.GetDescendantByPath(chainNonTerminalIds);
			}
		}

		private void FindWhereGroupByHavingReferences(OracleQueryBlock queryBlock)
		{
			queryBlock.WhereClause = queryBlock.RootNode.GetDescendantByPath(NonTerminals.WhereClause);
			if (queryBlock.WhereClause != null)
			{
				var whereClauseIdentifiers = queryBlock.WhereClause.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
				ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, whereClauseIdentifiers, StatementPlacement.Where, null);

				var whereClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(queryBlock.WhereClause);
				CreateGrammarSpecificFunctionReferences(whereClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.Where, null);
			}

			queryBlock.GroupByClause = queryBlock.RootNode.GetDescendantByPath(NonTerminals.GroupByClause);
			if (queryBlock.GroupByClause == null)
			{
				return;
			}

			var identifiers = queryBlock.GroupByClause.GetPathFilterDescendants(n => !n.Id.In(NonTerminals.NestedQuery, NonTerminals.HavingClause), Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, StatementPlacement.GroupBy, null);

			var groupByClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(queryBlock.GroupByClause, n => n.Id != NonTerminals.HavingClause);
			CreateGrammarSpecificFunctionReferences(groupByClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.GroupBy, null);

			queryBlock.HavingClause = queryBlock.GroupByClause.GetDescendantByPath(NonTerminals.HavingClause);
			if (queryBlock.HavingClause == null)
			{
				return;
			}

			identifiers = queryBlock.HavingClause.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, StatementPlacement.Having, null);

			var havingClauseGrammarSpecificFunctions = GetGrammarSpecificFunctionNodes(queryBlock.HavingClause);
			CreateGrammarSpecificFunctionReferences(havingClauseGrammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.Having, null);
		}

		private void ResolveOrderByReferences(OracleQueryBlock queryBlock)
		{
			if (queryBlock.PrecedingConcatenatedQueryBlock != null)
				return;

			queryBlock.OrderByClause = queryBlock.RootNode.ParentNode.GetPathFilterDescendants(n => n.Id != NonTerminals.QueryBlock, NonTerminals.OrderByClause).FirstOrDefault();
			if (queryBlock.OrderByClause == null)
				return;

			var identifiers = queryBlock.OrderByClause.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, StatementPlacement.OrderBy, null);
			var grammarSpecificFunctions = GetGrammarSpecificFunctionNodes(queryBlock.OrderByClause);
			CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, StatementPlacement.OrderBy, null);
		}

		private IEnumerable<StatementGrammarNode> GetGrammarSpecificFunctionNodes(StatementGrammarNode sourceNode, Func<StatementGrammarNode, bool> filter = null)
		{
			return sourceNode.GetPathFilterDescendants(n => NodeFilters.BreakAtNestedQueryBoundary(n) && (filter == null || filter(n)),
				Terminals.Count, Terminals.NegationOrNull, NonTerminals.AggregateFunction, NonTerminals.AnalyticFunction, NonTerminals.WithinGroupAggregationFunction);
		}

		private void ResolveColumnAndFunctionReferenceFromIdentifiers(OracleQueryBlock queryBlock, OracleReferenceContainer referenceContainer, IEnumerable<StatementGrammarNode> identifiers, StatementPlacement placement, OracleSelectListColumn selectListColumn, Func<StatementGrammarNode, StatementGrammarNode> getPrefixNonTerminalFromIdentiferFunction = null)
		{
			foreach (var identifier in identifiers)
			{
				var prefixNonTerminal = getPrefixNonTerminalFromIdentiferFunction == null
					? GetPrefixNodeFromPrefixedColumnReference(identifier)
					: getPrefixNonTerminalFromIdentiferFunction(identifier);

				var functionCallNodes = GetFunctionCallNodes(identifier);
				var hasNotDatabaseLink = GetDatabaseLinkFromIdentifier(identifier) == null;
				var isSequencePseudoColumnCandidate = prefixNonTerminal != null && identifier.Token.Value.ToQuotedIdentifier().In(OracleSequence.NormalizedColumnNameNextValue, OracleSequence.NormalizedColumnNameCurrentValue);
				if (functionCallNodes.Length == 0 && (hasNotDatabaseLink || isSequencePseudoColumnCandidate))
				{
					var columnReference = CreateColumnReference(referenceContainer, queryBlock, selectListColumn, placement, identifier, prefixNonTerminal);
					referenceContainer.ColumnReferences.Add(columnReference);
				}
				else
				{
					var programReference = CreateProgramReference(referenceContainer, queryBlock, selectListColumn, placement, identifier, prefixNonTerminal, functionCallNodes);
					referenceContainer.ProgramReferences.Add(programReference);
				}
			}
		}

		private StatementGrammarNode GetPrefixNodeFromPrefixedColumnReference(StatementGrammarNode identifier)
		{
			var prefixedColumnReferenceNode = identifier.GetPathFilterAncestor(n => n.Id != NonTerminals.Expression, NonTerminals.PrefixedColumnReference);
			return prefixedColumnReferenceNode == null
				? null
				: prefixedColumnReferenceNode.GetDescendantByPath(NonTerminals.Prefix);
		}

		private void FindSelectListReferences(OracleQueryBlock queryBlock)
		{
			queryBlock.SelectList = queryBlock.RootNode.GetDescendantByPath(NonTerminals.SelectList);
			if (queryBlock.SelectList == null)
				return;

			var distinctModifierNode = queryBlock.RootNode.GetDescendantByPath(NonTerminals.DistinctModifier);
			queryBlock.HasDistinctResultSet = distinctModifierNode != null && distinctModifierNode.FirstTerminalNode.Id.In(Terminals.Distinct, Terminals.Unique);

			if (queryBlock.SelectList.FirstTerminalNode == null)
				return;

			if (queryBlock.Type == QueryBlockType.CommonTableExpression)
			{
				queryBlock.ExplicitColumnNameList = queryBlock.AliasNode.ParentNode.GetDescendantByPath(NonTerminals.ParenthesisEnclosedIdentifierList);
				if (queryBlock.ExplicitColumnNameList != null)
				{
					var commonTableExpressionExplicitColumnNameList = new List<string>(
						queryBlock.ExplicitColumnNameList.GetDescendants(Terminals.Identifier)
							.Select(t => t.Token.Value.ToQuotedIdentifier()));

					_commonTableExpressionExplicitColumnNames.Add(queryBlock, commonTableExpressionExplicitColumnNameList);
				}
			}

			if (queryBlock.SelectList.FirstTerminalNode.Id == Terminals.Asterisk)
			{
				var asteriskNode = queryBlock.SelectList.ChildNodes[0];
				var column = new OracleSelectListColumn(this, null)
				{
					RootNode = asteriskNode,
					Owner = queryBlock,
					IsAsterisk = true
				};

				column.ColumnReferences.Add(CreateColumnReference(column, queryBlock, column, StatementPlacement.SelectList, asteriskNode, null));

				_asteriskTableReferences[column] = new List<OracleDataObjectReference>(queryBlock.ObjectReferences);

				queryBlock.AddSelectListColumn(column);
			}
			else
			{
				var columnExpressions = GetAllChainedClausesByPath(queryBlock.SelectList.GetDescendantByPath(NonTerminals.AliasedExpressionOrAllTableColumns), n => n.ParentNode, NonTerminals.SelectExpressionExpressionChainedList, NonTerminals.AliasedExpressionOrAllTableColumns);
				var columnExpressionsIdentifierLookup = _queryBlockTerminals[queryBlock]
					.Where(t => t.SourcePosition.IndexStart >= queryBlock.SelectList.SourcePosition.IndexStart && t.SourcePosition.IndexEnd <= queryBlock.SelectList.SourcePosition.IndexEnd &&
					            (StandardIdentifierIds.Contains(t.Id) || t.ParentNode.Id.In(NonTerminals.AggregateFunction, NonTerminals.AnalyticFunction, NonTerminals.WithinGroupAggregationFunction)))
					.ToLookup(t => t.GetAncestor(NonTerminals.AliasedExpressionOrAllTableColumns));
				
				foreach (var columnExpression in columnExpressions)
				{
					var columnAliasNode = columnExpression.LastTerminalNode != null && columnExpression.LastTerminalNode.Id == Terminals.ColumnAlias
						? columnExpression.LastTerminalNode
						: null;

					var column =
						new OracleSelectListColumn(this, null)
						{
							AliasNode = columnAliasNode,
							RootNode = columnExpression,
							Owner = queryBlock
						};

					var asteriskNode = columnExpression.LastTerminalNode != null && columnExpression.LastTerminalNode.Id == Terminals.Asterisk
						? columnExpression.LastTerminalNode
						: null;
					
					if (asteriskNode != null)
					{
						column.IsAsterisk = true;

						var prefixNonTerminal = asteriskNode.ParentNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.Prefix);
						var columnReference = CreateColumnReference(column, queryBlock, column, StatementPlacement.SelectList, asteriskNode, prefixNonTerminal);
						column.ColumnReferences.Add(columnReference);

						var tableReferences = queryBlock.ObjectReferences.Where(t => t.FullyQualifiedObjectName == columnReference.FullyQualifiedObjectName || (columnReference.ObjectNode == null && t.FullyQualifiedObjectName.NormalizedName == columnReference.FullyQualifiedObjectName.NormalizedName));
						_asteriskTableReferences[column] = new List<OracleDataObjectReference>(tableReferences);
					}
					else
					{
						var columnExpressionIdentifiers = columnExpressionsIdentifierLookup[columnExpression].ToArray();
						var identifiers = columnExpressionIdentifiers.Where(t => t.Id.In(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Level)).ToArray();

						var previousColumnReferences = column.ColumnReferences.Count;
						ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, column, identifiers, StatementPlacement.SelectList, column);

						if (identifiers.Length == 1)
						{
							var parentExpression = identifiers[0].ParentNode.ParentNode.ParentNode;
							column.IsDirectReference = parentExpression.Id == NonTerminals.Expression && parentExpression.ChildNodes.Count == 1 && parentExpression.ParentNode.Id == NonTerminals.AliasedExpression;
						}
						
						var columnReferenceAdded = column.ColumnReferences.Count > previousColumnReferences;
						if (columnReferenceAdded && column.IsDirectReference && columnAliasNode == null)
						{
							column.AliasNode = identifiers[0];
						}

						var grammarSpecificFunctions = columnExpressionIdentifiers.Where(t => String.Equals(t.Id, Terminals.Count) || String.Equals(t.Id, Terminals.NegationOrNull))
							.Concat(columnExpressionIdentifiers.Where(t => t.ParentNode.Id.In(NonTerminals.AggregateFunction, NonTerminals.AnalyticFunction, NonTerminals.WithinGroupAggregationFunction)).Select(t => t.ParentNode));

						CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, queryBlock, column.ProgramReferences, StatementPlacement.SelectList, column);
					}

					queryBlock.AddSelectListColumn(column);
				}
			}
		}

		private static void CreateGrammarSpecificFunctionReferences(IEnumerable<StatementGrammarNode> grammarSpecificFunctions, OracleQueryBlock queryBlock, ICollection<OracleProgramReference> functionReferences, StatementPlacement placement, OracleSelectListColumn selectListColumn)
		{
			foreach (var identifierNode in grammarSpecificFunctions.Select(n => n.FirstTerminalNode).Distinct())
			{
				var rootNode = String.Equals(identifierNode.Id, Terminals.NegationOrNull)
					? identifierNode.GetAncestor(NonTerminals.Condition)
					: identifierNode.GetAncestor(NonTerminals.AnalyticFunctionCall) ?? identifierNode.GetAncestor(NonTerminals.WithinGroupAggregationClause) ?? identifierNode.GetAncestor(NonTerminals.AggregateFunctionCall);
				
				var analyticClauseNode = rootNode.GetDescendants(NonTerminals.AnalyticClause).FirstOrDefault();

				var parameterList = rootNode.ChildNodes.SingleOrDefault(n => n.Id.In(NonTerminals.ParenthesisEnclosedExpressionListWithMandatoryExpressions, NonTerminals.CountAsteriskParameter, NonTerminals.AggregateFunctionParameter, NonTerminals.ParenthesisEnclosedExpressionListWithIgnoreNulls, NonTerminals.ParenthesisEnclosedCondition));
				var parameterNodes = new List<StatementGrammarNode>();
				StatementGrammarNode firstParameterExpression = null;
				if (parameterList != null)
				{
					switch (parameterList.Id)
					{
						case NonTerminals.ParenthesisEnclosedCondition:
							parameterNodes.Add(parameterList.GetDescendantByPath(NonTerminals.Condition));
							break;
						case NonTerminals.CountAsteriskParameter:
							parameterNodes.Add(parameterList.GetDescendantByPath(Terminals.Asterisk));
							break;
						case NonTerminals.AggregateFunctionParameter:
						case NonTerminals.ParenthesisEnclosedExpressionListWithIgnoreNulls:
							firstParameterExpression = parameterList.GetDescendantByPath(NonTerminals.Expression);
							parameterNodes.Add(firstParameterExpression);
							goto default;
						default:
							var nodes = parameterList.GetPathFilterDescendants(n => n != firstParameterExpression && !n.Id.In(NonTerminals.NestedQuery, NonTerminals.ParenthesisEnclosedAggregationFunctionParameters), NonTerminals.ExpressionList, NonTerminals.OptionalParameterExpressionList)
								.Select(n => n.ChildNodes.FirstOrDefault());
							parameterNodes.AddRange(nodes);
							break;
					}
				}

				var functionReference =
					new OracleProgramReference
					{
						FunctionIdentifierNode = identifierNode,
						RootNode = rootNode,
						Owner = queryBlock,
						AnalyticClauseNode = analyticClauseNode,
						ParameterListNode = parameterList,
						ParameterReferences = parameterNodes
							.Select(n => new ProgramParameterReference {ParameterNode = n}).ToArray(),
						SelectListColumn = selectListColumn,
						Placement = placement
					};

				functionReferences.Add(functionReference);
			}
		}

		private static StatementGrammarNode[] GetFunctionCallNodes(StatementGrammarNode identifier)
		{
			return identifier.ParentNode.ChildNodes.Where(n => n.Id.In(NonTerminals.ParenthesisEnclosedAggregationFunctionParameters, NonTerminals.AnalyticClause)).ToArray();
		}

		private static OracleProgramReference CreateProgramReference(OracleReferenceContainer container, OracleQueryBlock queryBlock, OracleSelectListColumn selectListColumn, StatementPlacement placement, StatementGrammarNode identifierNode, StatementGrammarNode prefixNonTerminal, ICollection<StatementGrammarNode> functionCallNodes)
		{
			var analyticClauseNode = functionCallNodes.SingleOrDefault(n => n.Id == NonTerminals.AnalyticClause);

			var parameterList = functionCallNodes.SingleOrDefault(n => String.Equals(n.Id, NonTerminals.ParenthesisEnclosedAggregationFunctionParameters));

			IEnumerable<StatementGrammarNode> parameterExpressionRootNodes = StatementGrammarNode.EmptyArray;
			if (parameterList != null)
			{
				parameterExpressionRootNodes = parameterList
					.GetPathFilterDescendants(
						n => !n.Id.In(NonTerminals.NestedQuery, NonTerminals.ParenthesisEnclosedAggregationFunctionParameters, NonTerminals.AggregateFunctionCall, NonTerminals.AnalyticFunctionCall, NonTerminals.AnalyticClause),
						NonTerminals.ExpressionList, NonTerminals.OptionalParameterExpressionList)
					.Select(n => n.ChildNodes.FirstOrDefault());
			}

			var functionReference =
				new OracleProgramReference
				{
					FunctionIdentifierNode = identifierNode,
					DatabaseLinkNode = GetDatabaseLinkFromIdentifier(identifierNode),
					RootNode = identifierNode.GetAncestor(NonTerminals.Expression),
					Owner = queryBlock,
					Placement = placement,
					AnalyticClauseNode = analyticClauseNode,
					ParameterListNode = parameterList,
					ParameterReferences = parameterExpressionRootNodes
						.Select(n =>
							new ProgramParameterReference
							{
								ParameterNode = n,
								OptionalIdentifierTerminal = n.FirstTerminalNode != null && n.FirstTerminalNode.Id == Terminals.ParameterIdentifier ? n.FirstTerminalNode : null
							}).ToArray(),
					SelectListColumn = selectListColumn
				};

			functionReference.SetContainer(container);

			AddPrefixNodes(functionReference, prefixNonTerminal);

			return functionReference;
		}

		private static StatementGrammarNode GetDatabaseLinkFromQueryTableExpression(StatementGrammarNode queryTableExpression)
		{
			var partitionOrDatabaseLink = queryTableExpression.GetDescendantByPath(NonTerminals.PartitionOrDatabaseLink);
			return partitionOrDatabaseLink == null
				? null
				: GetDatabaseLinkFromNode(partitionOrDatabaseLink);
		}

		private static StatementGrammarNode GetDatabaseLinkFromIdentifier(StatementGrammarNode identifier)
		{
			return GetDatabaseLinkFromNode(identifier.ParentNode);
		}

		private static StatementGrammarNode GetDatabaseLinkFromNode(StatementGrammarNode node)
		{
			var databaseLink = node.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.DatabaseLink);
			return databaseLink == null ? null : databaseLink.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.DatabaseLinkName);
		}

		private static OracleColumnReference CreateColumnReference(OracleReferenceContainer container, OracleQueryBlock queryBlock, OracleSelectListColumn selectListColumn, StatementPlacement placement, StatementGrammarNode identifierNode, StatementGrammarNode prefixNonTerminal)
		{
			var columnReference =
				new OracleColumnReference(container)
				{
					RootNode = identifierNode.GetPathFilterAncestor(n => n.Id != NonTerminals.AliasedExpressionOrAllTableColumns, NonTerminals.PrefixedColumnReference)
					           ?? identifierNode.GetPathFilterAncestor(n => n.Id != NonTerminals.AliasedExpressionOrAllTableColumns, NonTerminals.PrefixedAsterisk),
					ColumnNode = identifierNode,
					DatabaseLinkNode = GetDatabaseLinkFromIdentifier(identifierNode),
					Placement = placement,
					Owner = queryBlock,
					SelectListColumn = selectListColumn
				};

			AddPrefixNodes(columnReference, prefixNonTerminal);

			return columnReference;
		}

		private static void AddPrefixNodes(OracleReference reference, StatementGrammarNode prefixNonTerminal)
		{
			if (prefixNonTerminal == null)
				return;

			reference.OwnerNode = prefixNonTerminal.GetSingleDescendant(Terminals.SchemaIdentifier);
			reference.ObjectNode = prefixNonTerminal.GetSingleDescendant(Terminals.ObjectIdentifier);
		}

		private IEnumerable<KeyValuePair<StatementGrammarNode, string>> GetCommonTableExpressionReferences(StatementGrammarNode node)
		{
			var queryRoot = node.GetAncestor(NonTerminals.NestedQuery);
			var subQueryCompondentNode = node.GetAncestor(NonTerminals.CommonTableExpressionList);
			var cteReferencesWithinSameClause = new List<KeyValuePair<StatementGrammarNode, string>>();
			if (subQueryCompondentNode != null)
			{
				var cteNodeWithinSameClause = subQueryCompondentNode.GetAncestor(NonTerminals.CommonTableExpressionList);
				while (cteNodeWithinSameClause != null)
				{
					cteReferencesWithinSameClause.Add(GetCteNameNode(cteNodeWithinSameClause));
					cteNodeWithinSameClause = cteNodeWithinSameClause.GetAncestor(NonTerminals.CommonTableExpressionList);
				}

				if (node.Level - queryRoot.Level > node.Level - subQueryCompondentNode.Level)
				{
					queryRoot = queryRoot.GetAncestor(NonTerminals.NestedQuery);
				}
			}

			if (queryRoot == null)
				return cteReferencesWithinSameClause;

			var commonTableExpressions = queryRoot
				.GetPathFilterDescendants(n => n.Id != NonTerminals.QueryBlock, NonTerminals.CommonTableExpressionList)
				.Select(GetCteNameNode);
			return commonTableExpressions
				.Concat(cteReferencesWithinSameClause)
				.Concat(GetCommonTableExpressionReferences(queryRoot));
		}

		private KeyValuePair<StatementGrammarNode, string> GetCteNameNode(StatementGrammarNode cteListNode)
		{
			var cteNode = cteListNode[0];
			var objectIdentifierNode = cteNode[0];
			var cteName = objectIdentifierNode == null ? null : objectIdentifierNode.Token.Value.ToQuotedIdentifier();
			return new KeyValuePair<StatementGrammarNode, string>(cteNode, cteName);
		}

		private OracleLiteral CreateLiteral(StatementGrammarNode terminal)
		{
			return
				new OracleLiteral
				{
					Terminal = terminal.FollowingTerminal,
					Type = terminal.Id == Terminals.Date ? LiteralType.Date : LiteralType.Timestamp
				};
		}
	}

	public enum ReferenceType
	{
		SchemaObject,
		CommonTableExpression,
		InlineView,
		TableCollection,
		XmlTable,
		JsonTable,
		SqlModel
	}

	public enum QueryBlockType
	{
		Normal,
		ScalarSubquery,
		CommonTableExpression
	}

	public class OracleInsertTarget : OracleReferenceContainer
	{
		public OracleInsertTarget(OracleStatementSemanticModel semanticModel) : base(semanticModel)
		{
		}

		public StatementGrammarNode RootNode { get; set; }

		public StatementGrammarNode TargetNode { get; set; }

		public StatementGrammarNode ColumnListNode { get; set; }
		
		public StatementGrammarNode ValueList { get; set; }

		public OracleQueryBlock RowSource { get; set; }

		public OracleDataObjectReference DataObjectReference { get { return ObjectReferences.Count == 1 ? ObjectReferences.First() : null; } }
	}
}
