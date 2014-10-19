using System;
using System.Collections.Generic;
using System.Linq;
using NonTerminals = SqlPad.Oracle.OracleGrammarDescription.NonTerminals;
using Terminals = SqlPad.Oracle.OracleGrammarDescription.Terminals;

namespace SqlPad.Oracle
{
	public class OracleStatementSemanticModel : IStatementSemanticModel
	{
		private Dictionary<StatementGrammarNode, OracleQueryBlock> _queryBlockNodes = new Dictionary<StatementGrammarNode, OracleQueryBlock>();
		private readonly List<OracleInsertTarget> _insertTargets = new List<OracleInsertTarget>();
		private readonly HashSet<StatementGrammarNode> _redundantTerminals = new HashSet<StatementGrammarNode>();
		private readonly OracleDatabaseModelBase _databaseModel;
		private readonly Dictionary<OracleSelectListColumn, ICollection<OracleDataObjectReference>> _asteriskTableReferences = new Dictionary<OracleSelectListColumn, ICollection<OracleDataObjectReference>>();
		private readonly List<ICollection<OracleColumnReference>> _joinClauseColumnReferences = new List<ICollection<OracleColumnReference>>();
		private readonly Dictionary<OracleQueryBlock, ICollection<StatementGrammarNode>> _accessibleQueryBlockRoot = new Dictionary<OracleQueryBlock, ICollection<StatementGrammarNode>>();
		private readonly Dictionary<OracleDataObjectReference, ICollection<KeyValuePair<StatementGrammarNode, string>>> _objectReferenceCteRootNodes = new Dictionary<OracleDataObjectReference, ICollection<KeyValuePair<StatementGrammarNode, string>>>();

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

		public ICollection<StatementGrammarNode> RedundantNodes { get { return _redundantTerminals; } } 

		public OracleMainObjectReferenceContainer MainObjectReferenceContainer { get; private set; }

		public IEnumerable<OracleReferenceContainer> AllReferenceContainers
		{
			get
			{
				return ((IEnumerable<OracleReferenceContainer>)_queryBlockNodes.Values)
					.Concat(_queryBlockNodes.Values.SelectMany(qb => qb.Columns))
					.Concat(_insertTargets)
					.Concat(Enumerable.Repeat(MainObjectReferenceContainer, 1));
			}
		}

		public OracleQueryBlock MainQueryBlock
		{
			get
			{
				return _queryBlockNodes.Values
					.Where(qb => qb.Type == QueryBlockType.Normal)
					.OrderBy(qb => qb.RootNode.SourcePosition.IndexStart)
					.FirstOrDefault();
			}
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

		private void Build()
		{
			_queryBlockNodes = Statement.RootNode.GetDescendants(NonTerminals.QueryBlock)
				.OrderByDescending(q => q.Level)
				.ToDictionary(n => n, n => new OracleQueryBlock(this) { RootNode = n, Statement = Statement });

			foreach (var kvp in _queryBlockNodes)
			{
				var queryBlockRoot = kvp.Key;
				var queryBlock = kvp.Value;

				var scalarSubqueryExpression = queryBlockRoot.GetAncestor(NonTerminals.Expression);
				if (scalarSubqueryExpression != null)
				{
					queryBlock.Type = QueryBlockType.ScalarSubquery;
				}

				var commonTableExpression = queryBlockRoot.GetPathFilterAncestor(NodeFilters.BreakAtNestedQueryBoundary, NonTerminals.SubqueryComponent);
				if (commonTableExpression != null)
				{
					queryBlock.AliasNode = commonTableExpression.ChildNodes.First();
					queryBlock.Type = QueryBlockType.CommonTableExpression;
				}
				else
				{
					var selfTableReference = queryBlockRoot.GetAncestor(NonTerminals.TableReference);
					if (selfTableReference != null)
					{
						queryBlock.Type = QueryBlockType.Normal;

						var nestedSubqueryAlias = selfTableReference.ChildNodes.SingleOrDefault(n => n.Id == Terminals.ObjectAlias);
						if (nestedSubqueryAlias != null)
						{
							queryBlock.AliasNode = nestedSubqueryAlias;
						}
					}
				}

				var fromClause = queryBlockRoot.GetDescendantsWithinSameQuery(NonTerminals.FromClause).FirstOrDefault();
				var tableReferenceNonterminals = fromClause == null
					? Enumerable.Empty<StatementGrammarNode>()
					: fromClause.GetDescendantsWithinSameQuery(NonTerminals.TableReference).ToArray();

				// TODO: Check possible issue within GetCommonTableExpressionReferences to remove the Distinct.
				var cteReferences = GetCommonTableExpressionReferences(queryBlockRoot).Distinct().ToDictionary(qb => qb.Key, qb => qb.Value);
				_accessibleQueryBlockRoot.Add(queryBlock, cteReferences.Keys);

				foreach (var tableReferenceNonterminal in tableReferenceNonterminals)
				{
					var queryTableExpression = tableReferenceNonterminal.GetDescendantsWithinSameQuery(NonTerminals.QueryTableExpression).SingleOrDefault();
					if (queryTableExpression == null)
						continue;

					var objectReferenceAlias = tableReferenceNonterminal.GetDescendantsWithinSameQuery(Terminals.ObjectAlias).SingleOrDefault();

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

					var tableIdentifierNode = queryTableExpression.GetDescendantByPath(Terminals.ObjectIdentifier);
					if (tableIdentifierNode == null)
						continue;

					var schemaPrefixNode = queryTableExpression.GetDescendantByPath(NonTerminals.SchemaPrefix);
					if (schemaPrefixNode != null)
					{
						schemaPrefixNode = schemaPrefixNode.ChildNodes.First();
					}

					var tableName = tableIdentifierNode.Token.Value.ToQuotedIdentifier();
					var commonTableExpressions = schemaPrefixNode != null
						? (ICollection<KeyValuePair<StatementGrammarNode, string>>)new Dictionary<StatementGrammarNode, string>()
						: cteReferences.Where(n => n.Value == tableName).ToArray();

					var referenceType = ReferenceType.CommonTableExpression;

					OracleSchemaObject schemaObject = null;
					if (commonTableExpressions.Count == 0)
					{
						referenceType = ReferenceType.SchemaObject;

						var objectName = tableIdentifierNode.Token.Value;
						var owner = schemaPrefixNode == null ? null : schemaPrefixNode.Token.Value;

						if (!IsSimpleModel)
						{
							schemaObject = _databaseModel.GetFirstSchemaObject<OracleDataObject>(_databaseModel.GetPotentialSchemaObjectIdentifiers(owner, objectName));
						}
					}

					var objectReference =
						new OracleDataObjectReference(referenceType)
						{
							Owner = queryBlock,
							RootNode = tableReferenceNonterminal,
							OwnerNode = schemaPrefixNode,
							ObjectNode = tableIdentifierNode,
							DatabaseLinkNode = GetDatabaseLinkFromQueryTableExpression(tableIdentifierNode.ParentNode),
							AliasNode = objectReferenceAlias,
							SchemaObject = schemaObject
						};

					queryBlock.ObjectReferences.Add(objectReference);

					if (commonTableExpressions.Count > 0)
					{
						_objectReferenceCteRootNodes[objectReference] = commonTableExpressions;
					}
				}

				FindSelectListReferences(queryBlock);

				FindWhereGroupByHavingReferences(queryBlock);

				FindJoinColumnReferences(queryBlock);
			}

			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				ResolveConcatenatedQueryBlocks(queryBlock);

				ResolveParentCorrelatedQueryBlock(queryBlock);

				foreach (var nestedQueryReference in queryBlock.ObjectReferences.Where(t => t.Type != ReferenceType.SchemaObject))
				{
					if (nestedQueryReference.Type == ReferenceType.InlineView)
					{
						nestedQueryReference.QueryBlocks.Add(_queryBlockNodes[nestedQueryReference.ObjectNode]);
					}
					else if (_objectReferenceCteRootNodes.ContainsKey(nestedQueryReference))
					{
						var commonTableExpressionNode = _objectReferenceCteRootNodes[nestedQueryReference];
						foreach (var referencedQueryBlock in commonTableExpressionNode
							.Where(nodeName => OracleObjectIdentifier.Create(null, nodeName.Value) == OracleObjectIdentifier.Create(null, nestedQueryReference.ObjectNode.Token.Value)))
						{
							var cteQueryBlockNode = referencedQueryBlock.Key.GetDescendants(NonTerminals.QueryBlock).FirstOrDefault();
							if (cteQueryBlockNode != null)
							{
								nestedQueryReference.QueryBlocks.Add(_queryBlockNodes[cteQueryBlockNode]);
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

			ExposeAsteriskColumns();

			ResolveReferences();

			BuildDmlModel();
			
			ResolveRedundantTerminals();
		}

		private void ResolveRedundantTerminals()
		{
			ResolveRedundantQualifiers();

			ResolveRedundantSelectListColumns();
		}

		private void ResolveRedundantSelectListColumns()
		{
			foreach (var queryBlock in _queryBlockNodes.Values.Where(qb => qb != MainQueryBlock))
			{
				var redundantColumns = 0;
				foreach (var column in queryBlock.Columns.Where(c => c.ExplicitDefinition && !c.IsReferenced))
				{
					if (++redundantColumns == queryBlock.Columns.Count)
					{
						break;
					}

					var initialPrecedingQueryBlock = queryBlock.AllPrecedingConcatenatedQueryBlocks.LastOrDefault();
					if (initialPrecedingQueryBlock != null)
					{
						var initialQueryBlockColumn = initialPrecedingQueryBlock.Columns
							.Where(c => c.ExplicitDefinition)
							.Skip(redundantColumns - 1)
							.FirstOrDefault();

						if (initialQueryBlockColumn != null && (initialQueryBlockColumn.IsReferenced || initialPrecedingQueryBlock == MainQueryBlock))
						{
							continue;
						}
					}

					_redundantTerminals.AddRange(column.RootNode.Terminals);

					if (!TryMakeRedundantIfComma(column.RootNode.PrecedingTerminal))
					{
						TryMakeRedundantIfComma(column.RootNode.FollowingTerminal);
					}
				}
			}
		}

		private bool TryMakeRedundantIfComma(StatementGrammarNode terminal)
		{
			return terminal != null && terminal.Id == Terminals.Comma && _redundantTerminals.Add(terminal);
		}

		private void ResolveRedundantQualifiers()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				var ownerNameObjectReferences = queryBlock.ObjectReferences
					.Where(o => o.OwnerNode != null && o.SchemaObject != null)
					.ToLookup(o => o.SchemaObject.Name);

				var removedObjectReferenceOwners = new HashSet<OracleDataObjectReference>();
				foreach (var ownerNameObjectReference in ownerNameObjectReferences)
				{
					var uniqueObjectReferenceCount = ownerNameObjectReference.Count();
					if (uniqueObjectReferenceCount != 1 || IsSimpleModel)
					{
						continue;
					}

					foreach (var objectReference in ownerNameObjectReference.Where(o => o.SchemaObject.Owner == DatabaseModel.CurrentSchema.ToQuotedIdentifier()))
					{
						var terminals = objectReference.RootNode.Terminals.TakeWhile(t => t != objectReference.ObjectNode);
						_redundantTerminals.AddRange(terminals);
						removedObjectReferenceOwners.Add(objectReference);
					}
				}

				var otherRedundantOwnerReferences = ((IEnumerable<OracleReference>)queryBlock.AllProgramReferences).Concat(queryBlock.AllTypeReferences).Concat(queryBlock.AllSequenceReferences)
					.Where(o => o.OwnerNode != null && IsSchemaObjectInCurrentSchemaOrAccessibleByPublicSynonym(o.SchemaObject));
				foreach (var reference in otherRedundantOwnerReferences)
				{
					var terminals = reference.RootNode.Terminals.TakeWhile(t => t != reference.ObjectNode);
					_redundantTerminals.AddRange(terminals);
				}

				foreach (var columnReference in queryBlock.AllColumnReferences.Where(c => c.ObjectNode != null && c.RootNode != null))
				{
					var uniqueObjectReferenceCount = queryBlock.ObjectReferences.Where(o => o.Columns.Any(c => c.Name == columnReference.NormalizedName)).Distinct().Count();
					if (uniqueObjectReferenceCount != 1)
					{
						if (columnReference.OwnerNode != null && removedObjectReferenceOwners.Contains(columnReference.ValidObjectReference))
						{
							var redundantSchemaPrefixTerminals = columnReference.RootNode.Terminals.TakeWhile(t => t != columnReference.ObjectNode);
							_redundantTerminals.AddRange(redundantSchemaPrefixTerminals);
						}

						continue;
					}

					var terminals = columnReference.RootNode.Terminals.TakeWhile(t => t != columnReference.ColumnNode);
					_redundantTerminals.AddRange(terminals);
				}
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

			var whereClauseRootNode = Statement.RootNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.WhereClause);
			if (whereClauseRootNode != null)
			{
				var whereClauseIdentifiers = whereClauseRootNode.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn);
				ResolveColumnAndFunctionReferenceFromIdentifiers(null, MainObjectReferenceContainer, whereClauseIdentifiers, QueryBlockPlacement.Where, null);
			}

			if (MainObjectReferenceContainer.MainObjectReference != null)
			{
				ResolveFunctionReferences(MainObjectReferenceContainer.ProgramReferences);
				ResolveColumnObjectReferences(MainObjectReferenceContainer.ColumnReferences, new [] { MainObjectReferenceContainer.MainObjectReference }, new OracleDataObjectReference[0]);
			}
		}

		private void ResolveMainObjectReferenceUpdateOrDelete()
		{
			var tableReferenceNode = Statement.RootNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.TableReference);
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
			else
			{
				MainObjectReferenceContainer.MainObjectReference = MainQueryBlock.SelfObjectReference;
			}

			if (Statement.RootNode.FirstTerminalNode.Id != Terminals.Update)
			{
				return;
			}

			var updateListNode = Statement.RootNode.GetDescendantByPath(NonTerminals.UpdateSetClause, NonTerminals.UpdateSetColumnsOrObjectValue);
			if (updateListNode == null)
			{
				return;
			}

			var identifiers = updateListNode.GetDescendantsWithinSameQuery(Terminals.Identifier);
			ResolveColumnAndFunctionReferenceFromIdentifiers(null, MainObjectReferenceContainer, identifiers, QueryBlockPlacement.None, null);
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
					var columnIdentiferNodes = insertTarget.ColumnListNode.GetDescendants(Terminals.Identifier);
					ResolveColumnAndFunctionReferenceFromIdentifiers(null, referenceContainer, columnIdentiferNodes, QueryBlockPlacement.None, null);
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
					ResolveColumnAndFunctionReferenceFromIdentifiers(null, insertTarget, identifiers, QueryBlockPlacement.None, null);
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

			var schemaPrefixNode = queryTableExpressionNode.ChildNodes[0].Id == Terminals.SchemaIdentifier
				? queryTableExpressionNode.ChildNodes[0]
				: null;

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

		private void ResolveParentCorrelatedQueryBlock(OracleQueryBlock queryBlock, bool allowMoreThanOneLevel = false)
		{
			var nestedQueryRoot = queryBlock.RootNode.ParentNode.ParentNode;
			
			foreach (var parentId in new[] { NonTerminals.Expression, NonTerminals.Condition })
			{
				var parentExpression = nestedQueryRoot.GetPathFilterAncestor(n => allowMoreThanOneLevel || n.Id != NonTerminals.NestedQuery, parentId);
				if (parentExpression == null)
					continue;
				
				queryBlock.ParentCorrelatedQueryBlock = GetQueryBlock(parentExpression);
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
		}

		private void ResolveJoinReferences()
		{
			foreach (var joinClauseColumnReferences in _joinClauseColumnReferences)
			{
				var queryBlock = joinClauseColumnReferences.First().Owner;
				foreach (var columnReference in joinClauseColumnReferences)
				{
					var fromClauseNode = columnReference.ColumnNode.GetAncestor(NonTerminals.FromClause);
					var relatedTableReferences = queryBlock.ObjectReferences
						.Where(t => t.RootNode.SourcePosition.IndexStart >= fromClauseNode.SourcePosition.IndexStart &&
									t.RootNode.SourcePosition.IndexEnd <= columnReference.ColumnNode.SourcePosition.IndexStart).ToArray();

					queryBlock.ColumnReferences.Add(columnReference);
					ResolveColumnObjectReferences(new [] { columnReference }, relatedTableReferences, new OracleDataObjectReference[0]);
				}
			}
		}

		private void ResolveReferences()
		{
			foreach (var queryBlock in _queryBlockNodes.Values)
			{
				ResolveOrderByReferences(queryBlock);

				ResolveFunctionReferences(queryBlock.AllProgramReferences);

				var parentCorrelatedQueryBlockObjectReferences = queryBlock.ParentCorrelatedQueryBlock == null
					? new OracleDataObjectReference[0]
					: queryBlock.ParentCorrelatedQueryBlock.ObjectReferences;

				var columnReferences = queryBlock.AllColumnReferences.Where(c => c.SelectListColumn == null || c.SelectListColumn.ExplicitDefinition);
				ResolveColumnObjectReferences(columnReferences, queryBlock.ObjectReferences, parentCorrelatedQueryBlockObjectReferences);

				ResolveDatabaseLinks(queryBlock);
			}

			ResolveJoinReferences();
		}

		private void ResolveDatabaseLinks(OracleQueryBlock queryBlock)
		{
			if (IsSimpleModel)
				return;

			foreach (var databaseLinkReference in queryBlock.DatabaseLinkReferences)
			{
				var databaseLinkName = String.Join(null, databaseLinkReference.DatabaseLinkNode.Terminals.Select(t => t.Token.Value));
				databaseLinkReference.DatabaseLink = _databaseModel.GetFirstDatabaseLink(_databaseModel.GetPotentialSchemaObjectIdentifiers(null, databaseLinkName));
			}
		}

		private void ExposeAsteriskColumns()
		{
			foreach (var asteriskTableReference in _asteriskTableReferences)
			{
				foreach (var objectReference in asteriskTableReference.Value)
				{
					IEnumerable<OracleSelectListColumn> exposedColumns;
					if (objectReference.Type == ReferenceType.SchemaObject)
					{
						var dataObject = objectReference.SchemaObject.GetTargetSchemaObject() as OracleDataObject;
						if (dataObject == null)
							continue;

						exposedColumns = dataObject.Columns.Values
							.Select(c => new OracleSelectListColumn(this)
							{
								ExplicitDefinition = false,
								IsDirectReference = true,
								ColumnDescription = c
							});
					}
					else
					{
						var columns = new List<OracleSelectListColumn>();
						foreach (var column in objectReference.QueryBlocks.SelectMany(qb => qb.Columns).Where(c => !c.IsAsterisk))
						{
							column.RegisterOuterReference();
							columns.Add(column.AsImplicit());
						}
						
						exposedColumns = columns;
					}

					var exposedColumnDictionary = new Dictionary<string, OracleColumnReference>();
					foreach (var exposedColumn in exposedColumns)
					{
						exposedColumn.Owner = asteriskTableReference.Key.Owner;

						OracleColumnReference columnReference;
						if (String.IsNullOrEmpty(exposedColumn.NormalizedName) || !exposedColumnDictionary.TryGetValue(exposedColumn.NormalizedName, out columnReference))
						{
							columnReference = CreateColumnReference(exposedColumn, exposedColumn.Owner, exposedColumn, QueryBlockPlacement.SelectList, asteriskTableReference.Key.RootNode.LastTerminalNode, null);

							if (!String.IsNullOrEmpty(exposedColumn.NormalizedName))
							{
								exposedColumnDictionary.Add(exposedColumn.NormalizedName, columnReference);
							}

							columnReference.ColumnNodeObjectReferences.Add(objectReference);
						}

						columnReference.ColumnNodeColumnReferences.Add(exposedColumn.ColumnDescription);

						exposedColumn.ColumnReferences.Add(columnReference);

						asteriskTableReference.Key.Owner.Columns.Add(exposedColumn);
					}
				}
			}
		}

		private void ResolveFunctionReferences(IEnumerable<OracleProgramReference> programReferences)
		{
			var functionsTransferredToTypes = new List<OracleProgramReference>();
			foreach (var functionReference in programReferences)
			{
				if (UpdateFunctionReferenceWithMetadata(functionReference) != null)
					continue;

				var typeReference = ResolveTypeReference(functionReference);
				if (typeReference == null)
					continue;

				functionsTransferredToTypes.Add(functionReference);
				functionReference.Container.TypeReferences.Add(typeReference);
			}

			functionsTransferredToTypes.ForEach(f => f.Container.ProgramReferences.Remove(f));
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
					ParameterNodes = functionReference.ParameterNodes,
					ParameterListNode = functionReference.ParameterListNode,
					RootNode = functionReference.RootNode,
					SchemaObject = schemaObject,
					SelectListColumn = functionReference.SelectListColumn,
					ObjectNode = functionReference.FunctionIdentifierNode
				};
		}

		private OracleFunctionMetadata UpdateFunctionReferenceWithMetadata(OracleProgramReference programReference)
		{
			if (IsSimpleModel)
				return null;

			var owner = String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner)
				? _databaseModel.CurrentSchema
				: programReference.FullyQualifiedObjectName.NormalizedOwner;

			var originalIdentifier = OracleFunctionIdentifier.CreateFromValues(owner, programReference.FullyQualifiedObjectName.NormalizedName, programReference.NormalizedName);
			var parameterCount = programReference.ParameterNodes == null ? 0 : programReference.ParameterNodes.Count;
			var result = _databaseModel.GetFunctionMetadata(originalIdentifier, parameterCount, true);
			if (result.Metadata == null && !String.IsNullOrEmpty(originalIdentifier.Package) && String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner))
			{
				var identifier = OracleFunctionIdentifier.CreateFromValues(originalIdentifier.Package, null, originalIdentifier.Name);
				result = _databaseModel.GetFunctionMetadata(identifier, parameterCount, false);
			}

			if (result.Metadata == null && String.IsNullOrEmpty(programReference.FullyQualifiedObjectName.NormalizedOwner))
			{
				var identifier = OracleFunctionIdentifier.CreateFromValues(OracleDatabaseModelBase.SchemaPublic, originalIdentifier.Package, originalIdentifier.Name);
				result = _databaseModel.GetFunctionMetadata(identifier, parameterCount, false);
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
			return AllReferenceContainers.SelectMany(c => c.ColumnReferences).SingleOrDefault(c => c.ColumnNode == columnIdentifer);
		}

		public OracleProgramReference GetProgramReference(StatementGrammarNode identifer)
		{
			return AllReferenceContainers.SelectMany(c => c.ProgramReferences).SingleOrDefault(c => c.FunctionIdentifierNode == identifer);
		}

		public OracleTypeReference GetTypeReference(StatementGrammarNode typeIdentifer)
		{
			return AllReferenceContainers.SelectMany(c => c.TypeReferences).SingleOrDefault(c => c.ObjectNode == typeIdentifer);
		}

		public OracleSequenceReference GetSequenceReference(StatementGrammarNode sequenceIdentifer)
		{
			return AllReferenceContainers.SelectMany(c => c.SequenceReferences).SingleOrDefault(c => c.ObjectNode == sequenceIdentifer);
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
				if (columnReference.Placement == QueryBlockPlacement.OrderBy)
				{
					if (columnReference.Owner.FollowingConcatenatedQueryBlock != null)
					{
						ResolveConcatenatedQueryBlockOrderByReferences(columnReference);

						continue;
					}

					var orderByColumnAliasOrAsteriskReferences = columnReference.ObjectNode == null
						? columnReference.Owner.Columns
							.Where(c => c.NormalizedName == columnReference.NormalizedName)
							.Select(c => c.ColumnDescription)
						: Enumerable.Empty<OracleColumn>();

					columnReference.ColumnNodeColumnReferences.AddRange(orderByColumnAliasOrAsteriskReferences);
				}

				ResolveColumnReference(accessibleRowSourceReferences, columnReference);
				if (columnReference.ColumnNodeObjectReferences.Count == 0)
				{
					ResolveColumnReference(parentCorrelatedRowSourceReferences, columnReference);
				}

				var referencesSelectListColumn = columnReference.Placement == QueryBlockPlacement.OrderBy && columnReference.ColumnNodeObjectReferences.Count == 0 &&
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

		private void ResolveColumnReference(IEnumerable<OracleDataObjectReference> rowSources, OracleColumnReference columnReference)
		{
			foreach (var rowSourceReference in rowSources)
			{
				if (columnReference.ObjectNode != null &&
				    (rowSourceReference.FullyQualifiedObjectName == columnReference.FullyQualifiedObjectName ||
				     (columnReference.OwnerNode == null &&
				      rowSourceReference.Type == ReferenceType.SchemaObject && rowSourceReference.FullyQualifiedObjectName.NormalizedName == columnReference.FullyQualifiedObjectName.NormalizedName)))
				{
					columnReference.ObjectNodeObjectReferences.Add(rowSourceReference);
				}

				if (columnReference.Placement == QueryBlockPlacement.OrderBy && columnReference.ColumnNodeColumnReferences.Count > 0)
				{
					continue;
				}
				
				AddColumnNodeColumnReferences(rowSourceReference, columnReference);
			}
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
					ParameterNodes = null
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

		private void AddColumnNodeColumnReferences(OracleDataObjectReference rowSourceReference, OracleColumnReference columnReference)
		{
			if (!String.IsNullOrEmpty(columnReference.FullyQualifiedObjectName.NormalizedName) &&
			    columnReference.ObjectNodeObjectReferences.Count == 0)
			{
				return;
			}

			var precedingReferenceCount = columnReference.ColumnNodeColumnReferences.Count;
			if (rowSourceReference.Type == ReferenceType.SchemaObject)
			{
				if (rowSourceReference.SchemaObject == null)
					return;

				var dataObject = (OracleDataObject)rowSourceReference.SchemaObject.GetTargetSchemaObject();
				var oracleTable = dataObject as OracleTable;
				if (columnReference.ColumnNode.Id == Terminals.RowIdPseudoColumn)
				{
					if (oracleTable != null && oracleTable.RowIdPseudoColumn != null &&
						(columnReference.ObjectNode == null || IsTableReferenceValid(columnReference, rowSourceReference)))
					{
						columnReference.ColumnNodeColumnReferences.Add(oracleTable.RowIdPseudoColumn);
					}
				}
				else
				{
					columnReference.ColumnNodeColumnReferences.AddRange(dataObject.Columns.Values
						.Where(c => c.Name == columnReference.NormalizedName && (columnReference.ObjectNode == null || IsTableReferenceValid(columnReference, rowSourceReference))));
				}
			}
			else
			{
				var selectListColumns = rowSourceReference.QueryBlocks.SelectMany(qb => qb.Columns)
					.Where(c => c.NormalizedName == columnReference.NormalizedName && (columnReference.ObjectNode == null || columnReference.FullyQualifiedObjectName.NormalizedName == rowSourceReference.FullyQualifiedObjectName.NormalizedName));

				foreach (var selectListColumn in selectListColumns)
				{
					columnReference.ColumnNodeColumnReferences.Add(selectListColumn.ColumnDescription);
					selectListColumn.RegisterOuterReference();
				}
			}

			if (columnReference.ColumnNodeColumnReferences.Count > precedingReferenceCount)
			{
				columnReference.ColumnNodeObjectReferences.Add(rowSourceReference);
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
			var fromClauses = queryBlock.RootNode.GetDescendantsWithinSameQuery(NonTerminals.FromClause);
			foreach (var fromClause in fromClauses)
			{
				var joinClauses = fromClause.GetPathFilterDescendants(n => n.Id != NonTerminals.NestedQuery && n.Id != NonTerminals.FromClause, NonTerminals.JoinClause);
				foreach (var joinClause in joinClauses)
				{
					var joinCondition = joinClause.GetPathFilterDescendants(n => n.Id != NonTerminals.JoinClause, NonTerminals.JoinColumnsOrCondition).SingleOrDefault();
					if (joinCondition == null)
						continue;

					var identifiers = joinCondition.GetDescendants(Terminals.Identifier);
					var joinReferenceContainer = new OracleMainObjectReferenceContainer(this);
					ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, joinReferenceContainer, identifiers, QueryBlockPlacement.Join, null);

					if (joinReferenceContainer.ColumnReferences.Count > 0)
					{
						_joinClauseColumnReferences.Add(joinReferenceContainer.ColumnReferences);
					}
				}
			}
		}

		private void FindWhereGroupByHavingReferences(OracleQueryBlock queryBlock)
		{
			var whereClauseRootNode = queryBlock.RootNode.GetDescendantsWithinSameQuery(NonTerminals.WhereClause).FirstOrDefault();
			if (whereClauseRootNode != null)
			{
				queryBlock.WhereClause = whereClauseRootNode;
				var whereClauseIdentifiers = whereClauseRootNode.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn);
				ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, whereClauseIdentifiers, QueryBlockPlacement.Where, null);
			}

			var groupByClauseRootNode = queryBlock.RootNode.GetDescendantsWithinSameQuery(NonTerminals.GroupByClause).FirstOrDefault();
			if (groupByClauseRootNode == null)
			{
				return;
			}
			
			queryBlock.GroupByClause = groupByClauseRootNode;
			var identifiers = groupByClauseRootNode.GetPathFilterDescendants(n => !n.Id.In(NonTerminals.NestedQuery, NonTerminals.HavingClause), Terminals.Identifier, Terminals.RowIdPseudoColumn);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, QueryBlockPlacement.GroupBy, null);

			var havingClauseRootNode = groupByClauseRootNode.GetDescendantsWithinSameQuery(NonTerminals.HavingClause).FirstOrDefault();
			if (havingClauseRootNode == null)
			{
				return;
			}

			queryBlock.HavingClause = havingClauseRootNode;
			identifiers = havingClauseRootNode.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, QueryBlockPlacement.Having, null);

			var grammarSpecificFunctions = havingClauseRootNode.GetDescendantsWithinSameQuery(Terminals.Count, NonTerminals.AggregateFunction, NonTerminals.AnalyticFunction).ToArray();
			CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, queryBlock, queryBlock.ProgramReferences, null);
		}

		private void ResolveOrderByReferences(OracleQueryBlock queryBlock)
		{
			if (queryBlock.PrecedingConcatenatedQueryBlock != null)
				return;

			queryBlock.OrderByClause = queryBlock.RootNode.ParentNode.GetPathFilterDescendants(n => n.Id != NonTerminals.QueryBlock, NonTerminals.OrderByClause).FirstOrDefault();
			if (queryBlock.OrderByClause == null)
				return;

			var identifiers = queryBlock.OrderByClause.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn);
			ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, queryBlock, identifiers, QueryBlockPlacement.OrderBy, null);
		}

		private void ResolveColumnAndFunctionReferenceFromIdentifiers(OracleQueryBlock queryBlock, OracleReferenceContainer referenceContainer, IEnumerable<StatementGrammarNode> identifiers, QueryBlockPlacement type, OracleSelectListColumn selectListColumn)
		{
			foreach (var identifier in identifiers)
			{
				var prefixNonTerminal = GetPrefixNodeFromPrefixedColumnReference(identifier);

				var functionCallNodes = GetFunctionCallNodes(identifier);
				if (functionCallNodes.Length == 0)
				{
					var columnReference = CreateColumnReference(referenceContainer, queryBlock, selectListColumn, type, identifier, prefixNonTerminal);
					referenceContainer.ColumnReferences.Add(columnReference);
				}
				else
				{
					var functionReference = CreateFunctionReference(referenceContainer, queryBlock, selectListColumn, identifier, prefixNonTerminal, functionCallNodes);
					referenceContainer.ProgramReferences.Add(functionReference);
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
			var queryBlockRoot = queryBlock.RootNode;

			queryBlock.SelectList = queryBlockRoot.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.SelectList);
			if (queryBlock.SelectList == null)
				return;

			var distinctModifierNode = queryBlockRoot.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.DistinctModifier);
			queryBlock.HasDistinctResultSet = distinctModifierNode != null && distinctModifierNode.FirstTerminalNode.Id.In(Terminals.Distinct, Terminals.Unique);

			if (queryBlock.SelectList.FirstTerminalNode == null)
				return;

			if (queryBlock.SelectList.FirstTerminalNode.Id == Terminals.Asterisk)
			{
				var asteriskNode = queryBlock.SelectList.ChildNodes[0];
				var column = new OracleSelectListColumn(this)
				{
					RootNode = asteriskNode,
					Owner = queryBlock,
					ExplicitDefinition = true,
					IsAsterisk = true
				};

				queryBlock.HasAsteriskClause = true;

				column.ColumnReferences.Add(CreateColumnReference(column, queryBlock, column, QueryBlockPlacement.SelectList, asteriskNode, null));

				_asteriskTableReferences[column] = new List<OracleDataObjectReference>(queryBlock.ObjectReferences);

				queryBlock.Columns.Add(column);
			}
			else
			{
				var columnExpressions = queryBlock.SelectList.GetDescendantsWithinSameQuery(NonTerminals.AliasedExpressionOrAllTableColumns).ToArray();
				foreach (var columnExpression in columnExpressions)
				{
					var columnAliasNode = columnExpression.LastTerminalNode != null && columnExpression.LastTerminalNode.Id == Terminals.ColumnAlias
						? columnExpression.LastTerminalNode
						: null;
					
					var column = new OracleSelectListColumn(this)
					{
						AliasNode = columnAliasNode,
						RootNode = columnExpression,
						Owner = queryBlock,
						ExplicitDefinition = true
					};

					var asteriskNode = columnExpression.LastTerminalNode != null && columnExpression.LastTerminalNode.Id == Terminals.Asterisk
						? columnExpression.LastTerminalNode
						: null;
					
					if (asteriskNode != null)
					{
						column.IsAsterisk = true;

						var prefixNonTerminal = asteriskNode.ParentNode.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.Prefix);
						var columnReference = CreateColumnReference(column, queryBlock, column, QueryBlockPlacement.SelectList, asteriskNode, prefixNonTerminal);
						column.ColumnReferences.Add(columnReference);

						var tableReferences = queryBlock.ObjectReferences.Where(t => t.FullyQualifiedObjectName == columnReference.FullyQualifiedObjectName || (columnReference.ObjectNode == null && t.FullyQualifiedObjectName.NormalizedName == columnReference.FullyQualifiedObjectName.NormalizedName));
						_asteriskTableReferences[column] = new List<OracleDataObjectReference>(tableReferences);
					}
					else
					{
						var columnGrammarLookup = columnExpression.GetDescendantsWithinSameQuery(Terminals.Identifier, Terminals.RowIdPseudoColumn, Terminals.Count, NonTerminals.AggregateFunction, NonTerminals.AnalyticFunction)
							.ToLookup(n => n.Id, n => n);

						var identifiers = columnGrammarLookup[Terminals.Identifier].Concat(columnGrammarLookup[Terminals.RowIdPseudoColumn]).ToArray();

						var previousColumnReferences = column.ColumnReferences.Count;
						ResolveColumnAndFunctionReferenceFromIdentifiers(queryBlock, column, identifiers, QueryBlockPlacement.SelectList, column);

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

						var grammarSpecificFunctions = columnGrammarLookup[Terminals.Count].Concat(columnGrammarLookup[NonTerminals.AggregateFunction]).Concat(columnGrammarLookup[NonTerminals.AnalyticFunction]);
						CreateGrammarSpecificFunctionReferences(grammarSpecificFunctions, queryBlock, column.ProgramReferences, column);
					}

					queryBlock.Columns.Add(column);
				}
			}
		}

		private static void CreateGrammarSpecificFunctionReferences(IEnumerable<StatementGrammarNode> grammarSpecificFunctions, OracleQueryBlock queryBlock, ICollection<OracleProgramReference> functionReferences, OracleSelectListColumn selectListColumn)
		{
			foreach (var identifierNode in grammarSpecificFunctions.Select(n => n.FirstTerminalNode).Distinct())
			{
				var rootNode = identifierNode.GetAncestor(NonTerminals.AnalyticFunctionCall) ?? identifierNode.GetAncestor(NonTerminals.AggregateFunctionCall);
				var analyticClauseNode = rootNode.GetSingleDescendant(NonTerminals.AnalyticClause);

				var parameterList = rootNode.ChildNodes.SingleOrDefault(n => n.Id.In(NonTerminals.ParenthesisEnclosedExpressionListWithMandatoryExpressions, NonTerminals.CountAsteriskParameter, NonTerminals.AggregateFunctionParameter, NonTerminals.ParenthesisEnclosedExpressionListWithIgnoreNulls));
				var parameterNodes = new List<StatementGrammarNode>();
				StatementGrammarNode firstParameterExpression = null;
				if (parameterList != null)
				{
					switch (parameterList.Id)
					{
						case NonTerminals.CountAsteriskParameter:
							parameterNodes.Add(parameterList.ChildNodes.SingleOrDefault(n => n.Id == Terminals.Asterisk));
							break;
						case NonTerminals.AggregateFunctionParameter:
						case NonTerminals.ParenthesisEnclosedExpressionListWithIgnoreNulls:
							firstParameterExpression = parameterList.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.Expression);
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
						ParameterNodes = parameterNodes.AsReadOnly(),
						SelectListColumn = selectListColumn
					};

				functionReferences.Add(functionReference);
			}
		}

		private static StatementGrammarNode[] GetFunctionCallNodes(StatementGrammarNode identifier)
		{
			return identifier.ParentNode.ChildNodes.Where(n => n.Id.In(NonTerminals.ParenthesisEnclosedAggregationFunctionParameters, NonTerminals.AnalyticClause)).ToArray();
		}

		private static OracleProgramReference CreateFunctionReference(OracleReferenceContainer container, OracleQueryBlock queryBlock, OracleSelectListColumn selectListColumn, StatementGrammarNode identifierNode, StatementGrammarNode prefixNonTerminal, ICollection<StatementGrammarNode> functionCallNodes)
		{
			var analyticClauseNode = functionCallNodes.SingleOrDefault(n => n.Id == NonTerminals.AnalyticClause);

			var parameterList = functionCallNodes.SingleOrDefault(n => n.Id == NonTerminals.ParenthesisEnclosedAggregationFunctionParameters);
			var parameterExpressionRootNodes = parameterList != null
				? parameterList
					.GetPathFilterDescendants(
						n => !n.Id.In(NonTerminals.NestedQuery, NonTerminals.ParenthesisEnclosedAggregationFunctionParameters, NonTerminals.AggregateFunctionCall, NonTerminals.AnalyticFunctionCall, NonTerminals.AnalyticClause),
						NonTerminals.ExpressionList, NonTerminals.OptionalParameterExpressionList)
					.Select(n => n.ChildNodes.FirstOrDefault())
					.ToArray()
				: null;

			var functionReference =
				new OracleProgramReference
				{
					FunctionIdentifierNode = identifierNode,
					DatabaseLinkNode = GetDatabaseLinkFromIdentifier(identifierNode),
					RootNode = identifierNode.GetAncestor(NonTerminals.Expression),
					Owner = queryBlock,
					AnalyticClauseNode = analyticClauseNode,
					ParameterListNode = parameterList,
					ParameterNodes = parameterExpressionRootNodes,
					SelectListColumn = selectListColumn
				};

			functionReference.SetContainer(container);

			AddPrefixNodes(functionReference, prefixNonTerminal);

			return functionReference;
		}

		private static StatementGrammarNode GetDatabaseLinkFromQueryTableExpression(StatementGrammarNode queryTableExpression)
		{
			var partitionOrDatabaseLink = queryTableExpression.ChildNodes.SingleOrDefault(n => n.Id == NonTerminals.PartitionOrDatabaseLink);
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

		private static OracleColumnReference CreateColumnReference(OracleReferenceContainer container, OracleQueryBlock queryBlock, OracleSelectListColumn selectListColumn, QueryBlockPlacement placement, StatementGrammarNode identifierNode, StatementGrammarNode prefixNonTerminal)
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
			var subQueryCompondentNode = node.GetAncestor(NonTerminals.SubqueryComponent);
			var cteReferencesWithinSameClause = new List<KeyValuePair<StatementGrammarNode, string>>();
			if (subQueryCompondentNode != null)
			{
				var cteNodeWithinSameClause = subQueryCompondentNode.GetAncestor(NonTerminals.SubqueryComponent);
				while (cteNodeWithinSameClause != null)
				{
					cteReferencesWithinSameClause.Add(GetCteNameNode(cteNodeWithinSameClause));
					cteNodeWithinSameClause = cteNodeWithinSameClause.GetAncestor(NonTerminals.SubqueryComponent);
				}

				if (node.Level - queryRoot.Level > node.Level - subQueryCompondentNode.Level)
				{
					queryRoot = queryRoot.GetAncestor(NonTerminals.NestedQuery);
				}
			}

			if (queryRoot == null)
				return cteReferencesWithinSameClause;

			var commonTableExpressions = queryRoot
				.GetPathFilterDescendants(n => n.Id != NonTerminals.QueryBlock, NonTerminals.SubqueryComponent)
				.Select(GetCteNameNode);
			return commonTableExpressions
				.Concat(cteReferencesWithinSameClause)
				.Concat(GetCommonTableExpressionReferences(queryRoot));
		}

		private KeyValuePair<StatementGrammarNode, string> GetCteNameNode(StatementGrammarNode cteNode)
		{
			var objectIdentifierNode = cteNode.ChildNodes.FirstOrDefault();
			var cteName = objectIdentifierNode == null ? null : objectIdentifierNode.Token.Value.ToQuotedIdentifier();
			return new KeyValuePair<StatementGrammarNode, string>(cteNode, cteName);
		}
	}

	public enum ReferenceType
	{
		SchemaObject,
		CommonTableExpression,
		InlineView
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
