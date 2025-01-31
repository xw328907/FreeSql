﻿using FreeSql.Internal.Model;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FreeSql.Internal.CommonProvider
{

    public abstract partial class Select0Provider
    {
        public int _limit, _skip;
        public string _select = "SELECT ", _orderby, _groupby, _having;
        public StringBuilder _where = new StringBuilder();
        public List<DbParameter> _params = new List<DbParameter>();
        public List<SelectTableInfo> _tables = new List<SelectTableInfo>();
        public List<Func<Type, string, string>> _tableRules = new List<Func<Type, string, string>>();
        public Func<Type, string, string> _aliasRule;
        public string _tosqlAppendContent;
        public StringBuilder _join = new StringBuilder();
        public IFreeSql _orm;
        public CommonUtils _commonUtils;
        public CommonExpression _commonExpression;
        public DbTransaction _transaction;
        public DbConnection _connection;
        public Action<object> _trackToList;
        public List<Action<object>> _includeToList = new List<Action<object>>();
#if net40
#else
        public List<Func<object, Task>> _includeToListAsync = new List<Func<object, Task>>();
#endif
        public bool _distinct;
        public Expression _selectExpression;
        public List<LambdaExpression> _whereCascadeExpression = new List<LambdaExpression>();
        public List<GlobalFilter.Item> _whereGlobalFilter;

        int _disposeCounter;
        ~Select0Provider()
        {
            if (Interlocked.Increment(ref _disposeCounter) != 1) return;
            _where.Clear();
            _params.Clear();
            _tables.Clear();
            _tableRules.Clear();
            _join.Clear();
            _trackToList = null;
            _includeToList.Clear();
#if net40
#else
            _includeToListAsync.Clear();
#endif
            _selectExpression = null;
            _whereCascadeExpression.Clear();
            _whereGlobalFilter = _orm.GlobalFilter.GetFilters();
            _whereCascadeExpression.AddRange(_whereGlobalFilter.Select(a => a.Where));
        }

        public static void CopyData(Select0Provider from, Select0Provider to, ReadOnlyCollection<ParameterExpression> lambParms)
        {
            if (to == null) return;
            to._limit = from._limit;
            to._skip = from._skip;
            to._select = from._select;
            to._orderby = from._orderby;
            to._groupby = from._groupby;
            to._having = from._having;
            to._where = new StringBuilder().Append(from._where.ToString());
            to._params = new List<DbParameter>(from._params.ToArray());

            if (lambParms == null)
                to._tables = new List<SelectTableInfo>(from._tables.ToArray());
            else
            {
                var findedIndexs = new List<int>();
                var _multiTables = to._tables;
                _multiTables[0] = from._tables[0];
                for (var a = 1; a < lambParms.Count; a++)
                {
                    var tbIndex = from._tables.FindIndex(b => b.Alias == lambParms[a].Name && b.Table.Type == lambParms[a].Type); ;
                    if (tbIndex != -1)
                    {
                        findedIndexs.Add(tbIndex);
                        _multiTables[a] = from._tables[tbIndex];
                    }
                    else
                    {
                        _multiTables[a].Alias = lambParms[a].Name;
                        _multiTables[a].Parameter = lambParms[a];
                    }
                }
                for (var a = 1; a < from._tables.Count; a++)
                {
                    if (findedIndexs.Contains(a)) continue;
                    _multiTables.Add(from._tables[a]);
                }
            }
            to._tableRules = new List<Func<Type, string, string>>(from._tableRules.ToArray());
            to._aliasRule = from._aliasRule;
            to._join = new StringBuilder().Append(from._join.ToString());
            //to._orm = from._orm;
            //to._commonUtils = from._commonUtils;
            //to._commonExpression = from._commonExpression;
            to._transaction = from._transaction;
            to._connection = from._connection;
            to._trackToList = from._trackToList;
            to._includeToList = new List<Action<object>>(from._includeToList.ToArray());
#if net40
#else
            to._includeToListAsync = new List<Func<object, Task>>(from._includeToListAsync.ToArray());
#endif
            to._distinct = from._distinct;
            to._selectExpression = from._selectExpression;
            to._whereCascadeExpression = new List<LambdaExpression>(from._whereCascadeExpression.ToArray());
            to._whereGlobalFilter = new List<GlobalFilter.Item>(from._whereGlobalFilter.ToArray());
        }
    }

    public abstract partial class Select0Provider<TSelect, T1> : Select0Provider, ISelect0<TSelect, T1> where TSelect : class where T1 : class
    {
        public Select0Provider(IFreeSql orm, CommonUtils commonUtils, CommonExpression commonExpression, object dywhere)
        {
            _orm = orm;
            _commonUtils = commonUtils;
            _commonExpression = commonExpression;
            _tables.Add(new SelectTableInfo { Table = _commonUtils.GetTableByEntity(typeof(T1)), Alias = "a", On = null, Type = SelectTableInfoType.From });
            this.Where(_commonUtils.WhereObject(_tables.First().Table, "a.", dywhere));
            if (_orm.CodeFirst.IsAutoSyncStructure && typeof(T1) != typeof(object)) _orm.CodeFirst.SyncStructure<T1>();
        }

        public TSelect TrackToList(Action<object> track)
        {
            _trackToList = track;
            return this as TSelect;
        }

        public TSelect WithTransaction(DbTransaction transaction)
        {
            _transaction = transaction;
            _connection = _transaction?.Connection;
            return this as TSelect;
        }
        public TSelect WithConnection(DbConnection connection)
        {
            if (_transaction?.Connection != connection) _transaction = null;
            _connection = connection;
            return this as TSelect;
        }

        public bool Any()
        {
            this.Limit(1);
            return this.ToList<int>($"{1}{_commonUtils.FieldAsAlias("as1")}").Sum() > 0; //这里的 Sum 为了分表查询
        }

        public long Count()
        {
            var tmpOrderBy = _orderby;
            _orderby = null; //解决 select count(1) from t order by id 这样的 SQL 错误
            try
            {
                return this.ToList<int>($"count(1){_commonUtils.FieldAsAlias("as1")}").Sum(); //这里的 Sum 为了分表查询
            }
            finally
            {
                _orderby = tmpOrderBy;
            }
        }

        public TSelect Count(out long count)
        {
            count = this.Count();
            return this as TSelect;
        }

        public TSelect GroupBy(string sql, object parms = null)
        {
            _groupby = sql;
            if (string.IsNullOrEmpty(_groupby)) return this as TSelect;
            _groupby = string.Concat(" \r\nGROUP BY ", _groupby);
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(_groupby, parms));
            return this as TSelect;
        }
        public TSelect Having(string sql, object parms = null)
        {
            if (string.IsNullOrEmpty(_groupby) || string.IsNullOrEmpty(sql)) return this as TSelect;
            _having = string.Concat(_having, " AND (", sql, ")");
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }

        public TSelect LeftJoin(Expression<Func<T1, bool>> exp)
        {
            if (exp == null) return this as TSelect;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.LeftJoin);
        }
        public TSelect InnerJoin(Expression<Func<T1, bool>> exp)
        {
            if (exp == null) return this as TSelect;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.InnerJoin);
        }
        public TSelect RightJoin(Expression<Func<T1, bool>> exp)
        {
            if (exp == null) return this as TSelect; _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.RightJoin);
        }
        public TSelect LeftJoin<T2>(Expression<Func<T1, T2, bool>> exp)
        {
            if (exp == null) return this as TSelect;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.LeftJoin);
        }
        public TSelect InnerJoin<T2>(Expression<Func<T1, T2, bool>> exp)
        {
            if (exp == null) return this as TSelect;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.InnerJoin);
        }
        public TSelect RightJoin<T2>(Expression<Func<T1, T2, bool>> exp)
        {
            if (exp == null) return this as TSelect;
            _tables[0].Parameter = exp.Parameters[0];
            return this.InternalJoin(exp?.Body, SelectTableInfoType.RightJoin);
        }

        public TSelect InnerJoin(string sql, object parms = null)
        {
            if (string.IsNullOrEmpty(sql)) return this as TSelect;
            _join.Append(" \r\nINNER JOIN ").Append(sql);
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }
        public TSelect LeftJoin(string sql, object parms = null)
        {
            if (string.IsNullOrEmpty(sql)) return this as TSelect;
            _join.Append(" \r\nLEFT JOIN ").Append(sql);
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }
        public TSelect RightJoin(string sql, object parms = null)
        {
            if (string.IsNullOrEmpty(sql)) return this as TSelect;
            _join.Append(" \r\nRIGHT JOIN ").Append(sql);
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }
        public TSelect RawJoin(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return this as TSelect;
            _join.Append(" \r\n").Append(sql);
            return this as TSelect;
        }

        public TSelect Limit(int limit)
        {
            _limit = limit;
            return this as TSelect;
        }
        public TSelect Master()
        {
            _select = $" {_select.Trim()} ";
            return this as TSelect;
        }
        public TSelect Offset(int offset) => this.Skip(offset) as TSelect;

        public TSelect OrderBy(string sql, object parms = null) => this.OrderBy(true, sql, parms);
        public TSelect OrderBy(bool condition, string sql, object parms = null)
        {
            if (condition == false) return this as TSelect;
            if (string.IsNullOrEmpty(sql)) _orderby = null;
            var isnull = string.IsNullOrEmpty(_orderby);
            _orderby = string.Concat(isnull ? " \r\nORDER BY " : "", _orderby, isnull ? "" : ", ", sql);
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }
        public TSelect Page(int pageNumber, int pageSize)
        {
            this.Skip(Math.Max(0, pageNumber - 1) * pageSize);
            return this.Limit(pageSize) as TSelect;
        }

        public TSelect Skip(int offset)
        {
            _skip = offset;
            return this as TSelect;
        }
        public TSelect Take(int limit) => this.Limit(limit) as TSelect;

        public TSelect Distinct()
        {
            _distinct = true;
            return this as TSelect;
        }

        public DataTable ToDataTable(string field = null)
        {
            var sql = this.ToSql(field);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            DataTable ret = null;
            Exception exception = null;
            try
            {
                ret = _orm.Ado.ExecuteDataTable(_connection, _transaction, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }

        public List<TTuple> ToList<TTuple>(string field)
        {
            var sql = this.ToSql(field);
            var type = typeof(TTuple);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<TTuple>();
            var flagStr = $"ToListField:{field}";
            Exception exception = null;
            try
            {
                _orm.Ado.ExecuteReader(_connection, _transaction, fetch =>
                {
                    var read = Utils.ExecuteArrayRowReadClassOrTuple(flagStr, type, null, fetch.Object, 0, _commonUtils);
                    ret.Add((TTuple)read.Value);
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }
        internal List<T1> ToListAfPrivate(string sql, GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<T1>();
            Exception exception = null;
            try
            {
                _orm.Ado.ExecuteReader(_connection, _transaction, fetch =>
                {
                    ret.Add(af.Read(_orm, fetch.Object));
                    if (otherData != null)
                    {
                        var idx = af.FieldCount - 1;
                        foreach (var other in otherData)
                            other.retlist.Add(_commonExpression.ReadAnonymous(other.read, fetch.Object, ref idx, false, null));
                    }
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            foreach (var include in _includeToList) include?.Invoke(ret);
            _trackToList?.Invoke(ret);
            return ret;
        }
        internal List<T1> ToListPrivate(GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            string sql = null;
            if (otherData?.Length > 0)
            {
                var sbField = new StringBuilder().Append(af.Field);
                foreach (var other in otherData)
                    sbField.Append(other.field);
                sql = this.ToSql(sbField.ToString());
            }
            else
                sql = this.ToSql(af.Field);

            return ToListAfPrivate(sql, af, otherData);
        }
        #region ToChunk
        internal void ToListAfChunkPrivate(int chunkSize, Action<FetchCallbackArgs<List<T1>>> chunkDone, string sql, GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new FetchCallbackArgs<List<T1>> { Object = new List<T1>() };
            var retCount = 0;
            Exception exception = null;
            var checkDoneTimes = 0;
            try
            {
                _orm.Ado.ExecuteReader(_connection, _transaction, fetch =>
                {
                    ret.Object.Add(af.Read(_orm, fetch.Object));
                    retCount++;
                    if (otherData != null)
                    {
                        var idx = af.FieldCount - 1;
                        foreach (var other in otherData)
                            other.retlist.Add(_commonExpression.ReadAnonymous(other.read, fetch.Object, ref idx, false, null));
                    }
                    if (chunkSize > 0 && chunkSize == ret.Object.Count)
                    {
                        checkDoneTimes++;

                        foreach (var include in _includeToList) include?.Invoke(ret.Object);
                        _trackToList?.Invoke(ret.Object);
                        chunkDone(ret);
                        fetch.IsBreak = ret.IsBreak;

                        ret.Object.Clear();
                        if (otherData != null)
                            foreach (var other in otherData)
                                other.retlist.Clear();
                    }
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, retCount);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            if (ret.Object.Any() || checkDoneTimes == 0)
            {
                foreach (var include in _includeToList) include?.Invoke(ret.Object);
                _trackToList?.Invoke(ret.Object);
                chunkDone(ret);
            }
        }
        internal void ToListChunkPrivate(int chunkSize, Action<FetchCallbackArgs<List<T1>>> chunkDone, GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            string sql = null;
            if (otherData?.Length > 0)
            {
                var sbField = new StringBuilder().Append(af.Field);
                foreach (var other in otherData)
                    sbField.Append(other.field);
                sql = this.ToSql(sbField.ToString());
            }
            else
                sql = this.ToSql(af.Field);

            ToListAfChunkPrivate(chunkSize, chunkDone, sql, af, otherData);
        }
        public void ToChunk(int size, Action<FetchCallbackArgs<List<T1>>> done, bool includeNestedMembers = false)
        {
            if (_selectExpression != null) throw new ArgumentException("Chunk 功能之前不可使用 Select");
            this.ToListChunkPrivate(size, done, includeNestedMembers == false ? this.GetAllFieldExpressionTreeLevel2() : this.GetAllFieldExpressionTreeLevelAll(), null);
        }
        #endregion
        public Dictionary<TKey, T1> ToDictionary<TKey>(Func<T1, TKey> keySelector) => ToDictionary(keySelector, a => a);
        public Dictionary<TKey, TElement> ToDictionary<TKey, TElement>(Func<T1, TKey> keySelector, Func<T1, TElement> elementSelector)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));
            var af = this.GetAllFieldExpressionTreeLevel2();
            var sql = this.ToSql(af.Field);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new Dictionary<TKey, TElement>();
            Exception exception = null;
            try
            {
                _orm.Ado.ExecuteReader(_connection, _transaction, fetch =>
                {
                    var item = af.Read(_orm, fetch.Object);
                    ret.Add(keySelector(item), elementSelector(item));
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            if (typeof(TElement) == typeof(T1)) _trackToList?.Invoke(ret.Values);
            return ret;
        }
        public virtual List<T1> ToList(bool includeNestedMembers = false)
        {
            if (_selectExpression != null) return this.InternalToList<T1>(_selectExpression);
            return this.ToListPrivate(includeNestedMembers == false ? this.GetAllFieldExpressionTreeLevel2() : this.GetAllFieldExpressionTreeLevelAll(), null);
        }
        public T1 ToOne()
        {
            this.Limit(1);
            return this.ToList().FirstOrDefault();
        }

        public T1 First() => this.ToOne();

        internal List<TReturn> ToListMrPrivate<TReturn>(string sql, ReadAnonymousTypeAfInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            var type = typeof(TReturn);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<TReturn>();
            Exception exception = null;
            try
            {
                _orm.Ado.ExecuteReader(_connection, _transaction, fetch =>
                {
                    var index = -1;
                    ret.Add((TReturn)_commonExpression.ReadAnonymous(af.map, fetch.Object, ref index, false, null));
                    if (otherData != null)
                        foreach (var other in otherData)
                            other.retlist.Add(_commonExpression.ReadAnonymous(other.read, fetch.Object, ref index, false, null));
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            if (typeof(TReturn) == typeof(T1))
                foreach (var include in _includeToList) include?.Invoke(ret);
            _trackToList?.Invoke(ret);
            return ret;
        }
        internal List<TReturn> ToListMapReaderPrivate<TReturn>(ReadAnonymousTypeAfInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            string sql = null;
            if (otherData?.Length > 0)
            {
                var sbField = new StringBuilder().Append(af.field);
                foreach (var other in otherData)
                    sbField.Append(other.field);
                sql = this.ToSql(sbField.ToString());
            }
            else
                sql = this.ToSql(af.field);

            return ToListMrPrivate<TReturn>(sql, af, otherData);
        }
        protected List<TReturn> ToListMapReader<TReturn>(ReadAnonymousTypeAfInfo af) => ToListMapReaderPrivate<TReturn>(af, null);
        protected ReadAnonymousTypeAfInfo GetExpressionField(Expression newexp, FieldAliasOptions fieldAlias = FieldAliasOptions.AsIndex)
        {
            var map = new ReadAnonymousTypeInfo();
            var field = new StringBuilder();
            var index = fieldAlias == FieldAliasOptions.AsProperty ? CommonExpression.ReadAnonymousFieldAsCsName : 0;

            _commonExpression.ReadAnonymousField(_tables, field, map, ref index, newexp, null, _whereCascadeExpression, true);
            return new ReadAnonymousTypeAfInfo(map, field.Length > 0 ? field.Remove(0, 2).ToString() : null);
        }
        static ConcurrentDictionary<string, GetAllFieldExpressionTreeInfo> _dicGetAllFieldExpressionTree = new ConcurrentDictionary<string, GetAllFieldExpressionTreeInfo>();
        public class GetAllFieldExpressionTreeInfo
        {
            public string Field { get; set; }
            public int FieldCount { get; set; }
            public Func<IFreeSql, DbDataReader, T1> Read { get; set; }
        }
        public GetAllFieldExpressionTreeInfo GetAllFieldExpressionTreeLevelAll()
        {
            return _dicGetAllFieldExpressionTree.GetOrAdd($"*{string.Join("+", _tables.Select(a => $"{_orm.Ado.DataType}-{a.Table.DbName}-{a.Alias}-{a.Type}"))}", s =>
            {
                var type = _tables.First().Table.TypeLazy ?? _tables.First().Table.Type;
                var ormExp = Expression.Parameter(typeof(IFreeSql), "orm");
                var rowExp = Expression.Parameter(typeof(DbDataReader), "row");
                var returnTarget = Expression.Label(type);
                var retExp = Expression.Variable(type, "ret");
                var dataIndexExp = Expression.Variable(typeof(int), "dataIndex");
                var readExp = Expression.Variable(typeof(Utils.RowInfo), "read");
                var readExpValue = Expression.MakeMemberAccess(readExp, Utils.RowInfo.PropertyValue);
                var readExpDataIndex = Expression.MakeMemberAccess(readExp, Utils.RowInfo.PropertyDataIndex);
                var blockExp = new List<Expression>();
                blockExp.AddRange(new Expression[] {
                    Expression.Assign(retExp, type.InternalNewExpression()),
                    Expression.Assign(dataIndexExp, Expression.Constant(0))
                });
                //typeof(Topic).GetMethod("get_Type").IsVirtual

                var field = new StringBuilder();
                var dicfield = new Dictionary<string, bool>();
                var tb = _tables.First();
                var index = 0;

                var tborder = new[] { tb }.Concat(_tables.ToArray().Where((a, b) => b > 0).OrderBy(a => a.Alias));
                var tbiindex = 0;
                foreach (var tbi in tborder)
                {
                    if (tbiindex > 0 && tbi.Type == SelectTableInfoType.From) continue;
                    if (tbiindex > 0 && tbi.Alias.StartsWith($"{tb.Alias}__") == false) continue;

                    var typei = tbi.Table.TypeLazy ?? tbi.Table.Type;
                    Expression curExp = retExp;

                    var colidx = 0;
                    foreach (var col in tbi.Table.Columns.Values)
                    {
                        if (index > 0)
                        {
                            field.Append(", ");
                            if (tbiindex > 0 && colidx == 0) field.Append("\r\n");
                        }
                        var quoteName = _commonUtils.QuoteSqlName(col.Attribute.Name);
                        field.Append(_commonUtils.QuoteReadColumn(col.CsType, col.Attribute.MapType, $"{tbi.Alias}.{quoteName}"));
                        ++index;
                        if (dicfield.ContainsKey(quoteName)) field.Append(_commonUtils.FieldAsAlias($"as{index}"));
                        else dicfield.Add(quoteName, true);
                        ++colidx;
                    }
                    tbiindex++;

                    if (tbiindex == 0)
                        blockExp.AddRange(new Expression[] {
                            Expression.Assign(readExp, Expression.Call(Utils.MethodExecuteArrayRowReadClassOrTuple, new Expression[] { Expression.Constant(null, typeof(string)), Expression.Constant(typei), Expression.Constant(null, typeof(int[])), rowExp, dataIndexExp, Expression.Constant(_commonUtils) })),
                            Expression.IfThen(
                                Expression.GreaterThan(readExpDataIndex, dataIndexExp),
                                Expression.Assign(dataIndexExp, readExpDataIndex)
                            ),
                            Expression.IfThen(
                                Expression.NotEqual(readExpValue, Expression.Constant(null)),
                                Expression.Assign(retExp, Expression.Convert(readExpValue, typei))
                            )
                        });
                    else
                    {
                        Expression curExpIfNotNull = Expression.IsTrue(Expression.Constant(true));
                        var curTb = tb;
                        var parentNameSplits = tbi.Alias.Split(new[] { "__" }, StringSplitOptions.None);
                        var iscontinue = false;
                        for (var k = 1; k < parentNameSplits.Length; k++)
                        {
                            var curPropName = parentNameSplits[k];
                            if (curTb.Table.Properties.TryGetValue(parentNameSplits[k], out var tryprop) == false)
                            {
                                k++;
                                curPropName = $"{curPropName}__{parentNameSplits[k]}";
                                if (curTb.Table.Properties.TryGetValue(parentNameSplits[k], out tryprop) == false)
                                {
                                    iscontinue = true;
                                    break;
                                }
                            }
                            curExp = Expression.MakeMemberAccess(curExp, tryprop);
                            if (k + 1 < parentNameSplits.Length)
                                curExpIfNotNull = Expression.AndAlso(curExpIfNotNull, Expression.NotEqual(curExp, Expression.Default(tryprop.PropertyType)));
                            curTb = _tables.Where(a => a.Alias == $"{curTb.Alias}__{curPropName}" && a.Table.Type == tryprop.PropertyType).FirstOrDefault();
                            if (curTb == null)
                            {
                                iscontinue = true;
                                break;
                            }
                        }
                        if (iscontinue) continue;

                        blockExp.Add(
                            Expression.IfThenElse(
                                curExpIfNotNull,
                                Expression.Block(new Expression[] {
                                    Expression.Assign(readExp, Expression.Call(Utils.MethodExecuteArrayRowReadClassOrTuple, new Expression[] { Expression.Constant(null, typeof(string)), Expression.Constant(typei), Expression.Constant(null, typeof(int[])), rowExp, dataIndexExp, Expression.Constant(_commonUtils) })),
                                    Expression.IfThen(
                                        Expression.GreaterThan(readExpDataIndex, dataIndexExp),
                                        Expression.Assign(dataIndexExp, readExpDataIndex)
                                    ),
                                    Expression.IfThenElse(
                                        Expression.NotEqual(readExpValue, Expression.Constant(null)),
                                        Expression.Assign(curExp, Expression.Convert(readExpValue, typei)),
                                        Expression.Assign(curExp, Expression.Constant(null, typei))
                                    )
                                }),
                                Expression.Block(
                                    Expression.Assign(readExpValue, Expression.Constant(null, typeof(object))),
                                    Expression.Assign(dataIndexExp, Expression.Constant(index))
                                )
                            )
                        );
                    }

                    if (tbi.Table.TypeLazy != null)
                        blockExp.Add(
                            Expression.IfThen(
                                Expression.NotEqual(readExpValue, Expression.Constant(null)),
                                Expression.Call(Expression.TypeAs(readExpValue, typei), tbi.Table.TypeLazySetOrm, ormExp)
                            )
                        ); //将 orm 传递给 lazy
                }

                blockExp.AddRange(new Expression[] {
                    Expression.Return(returnTarget, retExp),
                    Expression.Label(returnTarget, Expression.Default(type))
                });
                return new GetAllFieldExpressionTreeInfo
                {
                    Field = field.ToString(),
                    FieldCount = index,
                    Read = Expression.Lambda<Func<IFreeSql, DbDataReader, T1>>(Expression.Block(new[] { retExp, dataIndexExp, readExp }, blockExp), new[] { ormExp, rowExp }).Compile()
                };
            });
        }
        public GetAllFieldExpressionTreeInfo GetAllFieldExpressionTreeLevel2()
        {
            return _dicGetAllFieldExpressionTree.GetOrAdd(string.Join("+", _tables.Select(a => $"{_orm.Ado.DataType}-{a.Table.DbName}-{a.Alias}-{a.Type}")), s =>
            {
                var tb1 = _tables.First().Table;
                var type = tb1.TypeLazy ?? tb1.Type;
                var props = tb1.Properties;

                var ormExp = Expression.Parameter(typeof(IFreeSql), "orm");
                var rowExp = Expression.Parameter(typeof(DbDataReader), "row");
                var returnTarget = Expression.Label(type);
                var retExp = Expression.Variable(type, "ret");
                var dataIndexExp = Expression.Variable(typeof(int), "dataIndex");
                var readExp = Expression.Variable(typeof(Utils.RowInfo), "read");
                var readExpValue = Expression.MakeMemberAccess(readExp, Utils.RowInfo.PropertyValue);
                var readExpDataIndex = Expression.MakeMemberAccess(readExp, Utils.RowInfo.PropertyDataIndex);
                var blockExp = new List<Expression>();
                blockExp.AddRange(new Expression[] {
                    Expression.Assign(retExp, type.InternalNewExpression()),
                    Expression.Assign(dataIndexExp, Expression.Constant(0))
                });
                //typeof(Topic).GetMethod("get_Type").IsVirtual

                var field = new StringBuilder();
                var dicfield = new Dictionary<string, bool>();
                var tb = _tables.First();
                var index = 0;
                var otherindex = 0;
                foreach (var prop in props.Values)
                {
                    if (tb.Table.ColumnsByCsIgnore.ContainsKey(prop.Name)) continue;

                    if (tb.Table.ColumnsByCs.TryGetValue(prop.Name, out var col))
                    { //普通字段
                        if (index > 0) field.Append(", ");
                        var quoteName = _commonUtils.QuoteSqlName(col.Attribute.Name);
                        field.Append(_commonUtils.QuoteReadColumn(col.CsType, col.Attribute.MapType, $"{tb.Alias}.{quoteName}"));
                        ++index;
                        if (dicfield.ContainsKey(quoteName)) field.Append(_commonUtils.FieldAsAlias($"as{index}"));
                        else dicfield.Add(quoteName, true);
                    }
                    else
                    {
                        var tb2 = _tables.Where((a, b) => b > 0 &&
                            (a.Type == SelectTableInfoType.InnerJoin || a.Type == SelectTableInfoType.LeftJoin || a.Type == SelectTableInfoType.RightJoin) &&
                            string.IsNullOrEmpty(a.On) == false &&
                            a.Alias.StartsWith($"{tb.Alias}__") && //开头结尾完全匹配
                            a.Alias.EndsWith($"__{prop.Name}") //不清楚会不会有其他情况 求大佬优化
                            ).FirstOrDefault(); //判断 b > 0 防止 parent 递归关系
                        if (tb2 == null && props.Where(pw => pw.Value.PropertyType == prop.PropertyType).Count() == 1)
                            tb2 = _tables.Where((a, b) => b > 0 &&
                                (a.Type == SelectTableInfoType.InnerJoin || a.Type == SelectTableInfoType.LeftJoin || a.Type == SelectTableInfoType.RightJoin) &&
                                string.IsNullOrEmpty(a.On) == false &&
                                a.Table.Type == prop.PropertyType).FirstOrDefault();
                        if (tb2 == null) continue;
                        foreach (var col2 in tb2.Table.Columns.Values)
                        {
                            if (index > 0) field.Append(", ");
                            var quoteName = _commonUtils.QuoteSqlName(col2.Attribute.Name);
                            field.Append(_commonUtils.QuoteReadColumn(col2.CsType, col2.Attribute.MapType, $"{tb2.Alias}.{quoteName}"));
                            ++index;
                            ++otherindex;
                            if (dicfield.ContainsKey(quoteName)) field.Append(_commonUtils.FieldAsAlias($"as{index}"));
                            else dicfield.Add(quoteName, true);
                        }
                    }
                    //只读到二级属性
                    var propGetSetMethod = prop.GetSetMethod(true);
                    Expression readExpAssign = null; //加速缓存
                    if (prop.PropertyType.IsArray) readExpAssign = Expression.New(Utils.RowInfo.Constructor,
                        Utils.GetDataReaderValueBlockExpression(prop.PropertyType, Expression.Call(Utils.MethodDataReaderGetValue, new Expression[] { Expression.Constant(_commonUtils), rowExp, dataIndexExp })),
                        //Expression.Call(Utils.MethodGetDataReaderValue, new Expression[] { Expression.Constant(prop.PropertyType), Expression.Call(rowExp, Utils.MethodDataReaderGetValue, dataIndexExp) }),
                        Expression.Add(dataIndexExp, Expression.Constant(1))
                    );
                    else
                    {
                        var proptypeGeneric = prop.PropertyType;
                        if (proptypeGeneric.IsNullableType()) proptypeGeneric = proptypeGeneric.GetGenericArguments().First();
                        if (proptypeGeneric.IsEnum ||
                            Utils.dicExecuteArrayRowReadClassOrTuple.ContainsKey(proptypeGeneric)) readExpAssign = Expression.New(Utils.RowInfo.Constructor,
                                Utils.GetDataReaderValueBlockExpression(prop.PropertyType, Expression.Call(Utils.MethodDataReaderGetValue, new Expression[] { Expression.Constant(_commonUtils), rowExp, dataIndexExp })),
                                //Expression.Call(Utils.MethodGetDataReaderValue, new Expression[] { Expression.Constant(prop.PropertyType), Expression.Call(rowExp, Utils.MethodDataReaderGetValue, dataIndexExp) }),
                                Expression.Add(dataIndexExp, Expression.Constant(1))
                        );
                        else
                        {
                            var propLazyType = _commonUtils.GetTableByEntity(prop.PropertyType)?.TypeLazy ?? prop.PropertyType;
                            readExpAssign = Expression.Call(Utils.MethodExecuteArrayRowReadClassOrTuple, new Expression[] { Expression.Constant(null, typeof(string)), Expression.Constant(propLazyType), Expression.Constant(null, typeof(int[])), rowExp, dataIndexExp, Expression.Constant(_commonUtils) });
                        }
                    }
                    blockExp.AddRange(new Expression[] {
                        Expression.Assign(readExp, readExpAssign),
                        Expression.IfThen(Expression.GreaterThan(readExpDataIndex, dataIndexExp),
                            Expression.Assign(dataIndexExp, readExpDataIndex)),
						//Expression.Call(typeof(Trace).GetMethod("WriteLine", new Type[]{typeof(string)}), Expression.Call(typeof(string).GetMethod("Concat", new Type[]{typeof(object) }), readExpValue)),

						tb1.TypeLazy != null ?
                            Expression.IfThenElse(
                                Expression.NotEqual(readExpValue, Expression.Constant(null)),
                                Expression.Call(retExp, propGetSetMethod, Expression.Convert(readExpValue, prop.PropertyType)),
                                Expression.Call(retExp, propGetSetMethod, Expression.Convert(Utils.GetDataReaderValueBlockExpression(prop.PropertyType, Expression.Constant(null)), prop.PropertyType))
                            ) :
                            Expression.IfThen(
                                Expression.NotEqual(readExpValue, Expression.Constant(null)),
                                Expression.Call(retExp, propGetSetMethod, Expression.Convert(readExpValue, prop.PropertyType))
                            )
                    });
                }
                if (otherindex == 0)
                { //不读导航属性，优化单表读取性能
                    blockExp.Clear();
                    blockExp.AddRange(new Expression[] {
                        Expression.Assign(dataIndexExp, Expression.Constant(0)),
                        Expression.Assign(readExp, Expression.Call(Utils.MethodExecuteArrayRowReadClassOrTuple, new Expression[] { Expression.Constant(null, typeof(string)), Expression.Constant(type), Expression.Constant(null, typeof(int[])), rowExp, dataIndexExp, Expression.Constant(_commonUtils) })),
                        Expression.IfThen(
                            Expression.NotEqual(readExpValue, Expression.Constant(null)),
                            Expression.Assign(retExp, Expression.Convert(readExpValue, type))
                        )
                    });
                }
                if (tb1.TypeLazy != null)
                    blockExp.Add(
                        Expression.IfThen(
                            Expression.NotEqual(readExpValue, Expression.Constant(null)),
                            Expression.Call(retExp, tb1.TypeLazySetOrm, ormExp)
                        )
                    ); //将 orm 传递给 lazy
                blockExp.AddRange(new Expression[] {
                    Expression.Return(returnTarget, retExp),
                    Expression.Label(returnTarget, Expression.Default(type))
                });
                return new GetAllFieldExpressionTreeInfo
                {
                    Field = field.ToString(),
                    Read = Expression.Lambda<Func<IFreeSql, DbDataReader, T1>>(Expression.Block(new[] { retExp, dataIndexExp, readExp }, blockExp), new[] { ormExp, rowExp }).Compile()
                };
            });
        }
        protected ReadAnonymousTypeAfInfo GetAllFieldReflection()
        {
            var tb1 = _tables.First().Table;
            var type = tb1.TypeLazy ?? tb1.Type;
            var constructor = type.InternalGetTypeConstructor0OrFirst();
            var map = new ReadAnonymousTypeInfo { CsType = type, Consturctor = constructor, IsEntity = true };

            var field = new StringBuilder();
            var dicfield = new Dictionary<string, bool>();
            var tb = _tables.First();
            var index = 0;
            var ps = tb1.Properties;
            foreach (var p in ps.Values)
            {
                var child = new ReadAnonymousTypeInfo { Property = p, CsName = p.Name };
                if (tb.Table.ColumnsByCs.TryGetValue(p.Name, out var col))
                { //普通字段
                    if (index > 0) field.Append(", ");
                    var quoteName = _commonUtils.QuoteSqlName(col.Attribute.Name);
                    field.Append(_commonUtils.QuoteReadColumn(col.CsType, col.Attribute.MapType, $"{tb.Alias}.{quoteName}"));
                    ++index;
                    if (dicfield.ContainsKey(quoteName)) field.Append(_commonUtils.FieldAsAlias($"as{index}"));
                    else dicfield.Add(quoteName, true);
                }
                else
                {
                    var tb2 = _tables.Where(a => a.Table.Type == p.PropertyType && a.Alias.Contains(p.Name)).FirstOrDefault();
                    if (tb2 == null && ps.Where(pw => pw.Value.PropertyType == p.PropertyType).Count() == 1) tb2 = _tables.Where(a => a.Table.Type == p.PropertyType).FirstOrDefault();
                    if (tb2 == null) continue;
                    child.CsType = (tb2.Table.TypeLazy ?? tb2.Table.Type);
                    child.Consturctor = child.CsType.InternalGetTypeConstructor0OrFirst();
                    child.IsEntity = true;
                    foreach (var col2 in tb2.Table.Columns.Values)
                    {
                        if (index > 0) field.Append(", ");
                        var quoteName = _commonUtils.QuoteSqlName(col2.Attribute.Name);
                        field.Append(_commonUtils.QuoteReadColumn(col2.CsType, col2.Attribute.MapType, $"{tb2.Alias}.{quoteName}"));
                        ++index;
                        if (dicfield.ContainsKey(quoteName)) field.Append(_commonUtils.FieldAsAlias($"as{index}"));
                        else dicfield.Add(quoteName, true);
                        child.Childs.Add(new ReadAnonymousTypeInfo
                        {
                            Property = tb2.Table.Type.GetProperty(col2.CsName, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance),
                            CsName = col2.CsName
                        });
                    }
                }
                map.Childs.Add(child);
            }
            return new ReadAnonymousTypeAfInfo(map, field.ToString());
        }

        string GetToDeleteWhere(string alias)
        {
            var pks = _tables[0].Table.Primarys;
            var old_selectVal = _select;
            switch (_orm.Ado.DataType)
            {
                case DataType.Dameng:
                case DataType.OdbcDameng: //达梦不能这样
                case DataType.Oracle:
                case DataType.OdbcOracle:
                    break;
                default:
                    _select = "SELECT ";
                    break;
            }
            try
            {
                if (pks.Length == 1)
                    return $"{_commonUtils.QuoteSqlName(_tables[0].Table.Primarys[0].Attribute.Name)} in (select * from ({this.ToSql($"{_tables[0].Alias}.{_commonUtils.QuoteSqlName(_tables[0].Table.Primarys[0].Attribute.Name)}")}) {alias})";
                else
                {
                    var concatTypes = new Type[pks.Length * 2 - 1];
                    var concatMainCols = new string[pks.Length * 2 - 1];
                    var concatInCols = new string[pks.Length * 2 - 1];
                    var concatSplit = _commonUtils.FormatSql("{0}", $",{alias},");
                    for (var a = 0; a < pks.Length; a++)
                    {
                        concatTypes[a * 2] = pks[a].CsType;
                        concatMainCols[a * 2] = _commonUtils.QuoteSqlName(pks[a].Attribute.Name);
                        concatInCols[a * 2] = $"{_tables[0].Alias}.{_commonUtils.QuoteSqlName(pks[a].Attribute.Name)}";
                        if (a < pks.Length - 1)
                        {
                            concatTypes[a * 2 + 1] = typeof(string);
                            concatMainCols[a * 2 + 1] = concatSplit;
                            concatInCols[a * 2 + 1] = concatSplit;
                        }
                    }
                    return $"{_commonUtils.StringConcat(concatMainCols, concatTypes)} in (select * from ({this.ToSql($"{_commonUtils.StringConcat(concatInCols, concatTypes)} as as1")}) {alias})";
                }
            }
            finally
            {
                _select = old_selectVal;
            }
        }
        public IDelete<T1> ToDelete()
        {
            if (_tables[0].Table.Primarys.Any() == false) throw new Exception($"ToDelete 功能要求实体类 {_tables[0].Table.CsName} 必须有主键");
            var del = _orm.Delete<T1>() as DeleteProvider<T1>;
            if (_tables[0].Table.Type != typeof(T1)) del.AsType(_tables[0].Table.Type);
            if (_params.Any()) del.GetType().GetField("_params", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(del, new List<DbParameter>(_params.ToArray()));
            switch (_orm.Ado.DataType)
            {
                case DataType.Dameng:
                case DataType.OdbcDameng: //达梦不能这样
                case DataType.Oracle:
                case DataType.OdbcOracle:
                    break;
                default:
                    var beforeSql = this._select;
                    if (beforeSql.EndsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                    {
                        beforeSql = beforeSql.Substring(0, beforeSql.Length - 7);
                        if (string.IsNullOrEmpty(beforeSql) == false)
                            del._interceptSql = sb => sb.Insert(0, beforeSql);
                    }
                    break;
            }
            return del.Where(GetToDeleteWhere("ftb_del"));
        }
        public IUpdate<T1> ToUpdate()
        {
            if (_tables[0].Table.Primarys.Any() == false) throw new Exception($"ToUpdate 功能要求实体类 {_tables[0].Table.CsName} 必须有主键");
            var upd = _orm.Update<T1>() as UpdateProvider<T1>;
            if (_tables[0].Table.Type != typeof(T1)) upd.AsType(_tables[0].Table.Type);
            if (_params.Any()) upd.GetType().GetField("_params", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(upd, new List<DbParameter>(_params.ToArray()));
            switch (_orm.Ado.DataType)
            {
                case DataType.Dameng:
                case DataType.OdbcDameng: //达梦不能这样
                case DataType.Oracle:
                case DataType.OdbcOracle:
                    break;
                default:
                    var beforeSql = this._select;
                    if (beforeSql.EndsWith("SELECT ", StringComparison.OrdinalIgnoreCase))
                    {
                        beforeSql = beforeSql.Substring(0, beforeSql.Length - 7);
                        if (string.IsNullOrEmpty(beforeSql) == false)
                            upd._interceptSql = sb => sb.Insert(0, beforeSql);
                    }
                    break;
            }
            return upd.Where(GetToDeleteWhere("ftb_upd"));
        }

        protected List<Dictionary<Type, string>> GetTableRuleUnions()
        {
            var unions = new List<Dictionary<Type, string>>();
            var trs = _tableRules.Any() ? _tableRules : new List<Func<Type, string, string>>(new[] { new Func<Type, string, string>((type, oldname) => null) });
            foreach (var tr in trs)
            {
                var dict = new Dictionary<Type, string>();
                foreach (var tb in _tables)
                {
                    if (tb.Type == SelectTableInfoType.Parent) continue;
                    if (dict.ContainsKey(tb.Table.Type)) continue;
                    var name = tr?.Invoke(tb.Table.Type, tb.Table.DbName);
                    if (string.IsNullOrEmpty(name)) name = tb.Table.DbName;
                    else
                    {
                        if (name.IndexOf(' ') == -1) //还可以这样：select.AsTable((a, b) => "(select * from tb_topic where clicks > 10)").Page(1, 10).ToList()
                        {
                            if (_orm.CodeFirst.IsSyncStructureToLower) name = name.ToLower();
                            if (_orm.CodeFirst.IsSyncStructureToUpper) name = name.ToUpper();
                            if (_orm.CodeFirst.IsAutoSyncStructure) _orm.CodeFirst.SyncStructure(tb.Table.Type, name);
                        }
                        else
                            name = name.Replace(" \r\n", " \r\n    ");
                    }
                    dict.Add(tb.Table.Type, name);
                }
                unions.Add(dict);
            }
            return unions;
        }
        public TSelect AsTable(Func<Type, string, string> tableRule)
        {
            if (tableRule != null) _tableRules.Add(tableRule);
            return this as TSelect;
        }
        public TSelect AsAlias(Func<Type, string, string> aliasRule)
        {
            if (aliasRule != null) _aliasRule = aliasRule;
            return this as TSelect;
        }
        public TSelect AsType(Type entityType)
        {
            if (entityType == typeof(object)) throw new Exception("ISelect.AsType 参数不支持指定为 object");
            if (entityType == _tables[0].Table.Type) return this as TSelect;
            var newtb = _commonUtils.GetTableByEntity(entityType);
            _tables[0].Table = newtb ?? throw new Exception("ISelect.AsType 参数错误，请传入正确的实体类型");
            if (_orm.CodeFirst.IsAutoSyncStructure) _orm.CodeFirst.SyncStructure(entityType);
            return this as TSelect;
        }
        public abstract string ToSql(string field = null);

        public TSelect Where(string sql, object parms = null) => this.WhereIf(true, sql, parms);
        public TSelect WhereIf(bool condition, string sql, object parms = null)
        {
            if (condition == false || string.IsNullOrEmpty(sql)) return this as TSelect;
            _where.Append(" AND (").Append(sql).Append(")");
            if (parms != null) _params.AddRange(_commonUtils.GetDbParamtersByObject(sql, parms));
            return this as TSelect;
        }

        static MethodInfo MethodStringContains = typeof(string).GetMethod("Contains", new[] { typeof(string) });
        static MethodInfo MethodStringStartsWith = typeof(string).GetMethod("StartsWith", new[] { typeof(string) });
        static MethodInfo MethodStringEndsWith = typeof(string).GetMethod("EndsWith", new[] { typeof(string) });
        static ConcurrentDictionary<Type, MethodInfo> MethodEnumerableContainsDic = new ConcurrentDictionary<Type, MethodInfo>();
        static MethodInfo GetMethodEnumerableContains(Type elementType) => MethodEnumerableContainsDic.GetOrAdd(elementType, et => typeof(Enumerable).GetMethods().Where(a => a.Name == "Contains").FirstOrDefault().MakeGenericMethod(elementType));
        public TSelect WhereDynamicFilter(DynamicFilterInfo filter)
        {
            if (filter == null) return this as TSelect;
            var sb = new StringBuilder();
            ParseFilter(DynamicFilterLogic.And, filter, true);
            this.Where(sb.ToString());
            sb.Clear();
            return this as TSelect;

            void ParseFilter(DynamicFilterLogic logic, DynamicFilterInfo fi, bool isend)
            {
                if (string.IsNullOrEmpty(fi.Field) == false)
                {
                    var field = fi.Field.Split('.').Select(a => a.Trim()).ToArray();
                    Expression exp = null;

                    if (field.Length == 1)
                    {
                        foreach (var tb in _tables)
                        {
                            if (tb.Table.ColumnsByCs.TryGetValue(field[0], out var col) &&
                                tb.Table.Properties.TryGetValue(field[0], out var prop))
                            {
                                tb.Parameter = Expression.Parameter(tb.Table.Type, tb.Alias);
                                exp = Expression.MakeMemberAccess(tb.Parameter, prop);
                                break;
                            }
                        }
                        if (exp == null) throw new Exception($"无法匹配 {fi.Field}");
                    }
                    else
                    {
                        var firstTb = _tables[0];
                        var firstTbs = _tables.Where(a => a.AliasInit == field[0]).ToArray();
                        if (firstTbs.Length == 1) firstTb = firstTbs[0];

                        firstTb.Parameter = Expression.Parameter(firstTb.Table.Type, firstTb.Alias);
                        var currentType = firstTb.Table.Type;
                        Expression currentExp = firstTb.Parameter;

                        for (var x = 0; x < field.Length; x++)
                        {
                            var tmp1 = field[x];
                            if (_commonUtils.GetTableByEntity(currentType).Properties.TryGetValue(tmp1, out var prop) == false)
                                throw new ArgumentException($"{currentType.DisplayCsharp()} 无法找到属性名 {tmp1}");
                            currentType = prop.PropertyType;
                            currentExp = Expression.MakeMemberAccess(currentExp, prop);
                        }
                        exp = currentExp;
                    }

                    switch (fi.Operator)
                    {
                        case DynamicFilterOperator.Contains: exp = Expression.Call(exp, MethodStringContains, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.StartsWith: exp = Expression.Call(exp, MethodStringStartsWith, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.EndsWith: exp = Expression.Call(exp, MethodStringEndsWith, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.NotContains: exp = Expression.Not(Expression.Call(exp, MethodStringContains, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type))); break;
                        case DynamicFilterOperator.NotStartsWith: exp = Expression.Not(Expression.Call(exp, MethodStringStartsWith, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type))); break;
                        case DynamicFilterOperator.NotEndsWith: exp = Expression.Not(Expression.Call(exp, MethodStringEndsWith, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type))); break;

                        case DynamicFilterOperator.Eq:
                        case DynamicFilterOperator.Equals:
                        case DynamicFilterOperator.Equal: exp = Expression.Equal(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.NotEqual: exp = Expression.NotEqual(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;

                        case DynamicFilterOperator.GreaterThan: exp = Expression.GreaterThan(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.GreaterThanOrEqual: exp = Expression.GreaterThanOrEqual(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.LessThan: exp = Expression.LessThan(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.LessThanOrEqual: exp = Expression.LessThanOrEqual(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fi.Value), exp.Type)); break;
                        case DynamicFilterOperator.Range:
                            var fiValueRangeArray = getFiListValue();
                            if (fiValueRangeArray.Length != 2) throw new ArgumentException($"Range 要求 Value 应该逗号分割，并且长度为 2");
                            exp = Expression.AndAlso(
                                Expression.GreaterThanOrEqual(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fiValueRangeArray[0]), exp.Type)),
                                Expression.LessThan(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fiValueRangeArray[1]), exp.Type))); 
                            break;
                        case DynamicFilterOperator.DateRange:
                            var fiValueDateRangeArray = getFiListValue();
                            if (fiValueDateRangeArray?.Length != 2) throw new ArgumentException($"DateRange 要求 Value 应该逗号分割，并且长度为 2");
                            if (Regex.IsMatch(fiValueDateRangeArray[1], @"^\d\d\d\d[\-/]\d\d?[\-/]\d\d?$")) fiValueDateRangeArray[1] = DateTime.Parse(fiValueDateRangeArray[1]).AddDays(1).ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[1], @"^\d\d\d\d[\-/]\d\d?$")) fiValueDateRangeArray[1] = DateTime.Parse($"{fiValueDateRangeArray[1]}-01").AddMonths(1).ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[1], @"^\d\d\d\d$")) fiValueDateRangeArray[1] = DateTime.Parse($"{fiValueDateRangeArray[1]}-01-01").AddYears(1).ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[1], @"^\d\d\d\d[\-/]\d\d?[\-/]\d\d? \d\d?$")) fiValueDateRangeArray[1] = DateTime.Parse($"{fiValueDateRangeArray[1]}:00:00").AddHours(1).ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[1], @"^\d\d\d\d[\-/]\d\d?[\-/]\d\d? \d\d?:\d\d?$")) fiValueDateRangeArray[1] = DateTime.Parse($"{fiValueDateRangeArray[1]}:00").AddMinutes(1).ToString("yyyy-MM-dd HH:mm:ss");
                            else throw new ArgumentException($"DateRange 要求 Value[1] 格式必须为：yyyy、yyyy-MM、yyyy-MM-dd、yyyy-MM-dd HH、yyyy、yyyy-MM-dd HH:mm");

                            if (Regex.IsMatch(fiValueDateRangeArray[0], @"^\d\d\d\d[\-/]\d\d?$")) fiValueDateRangeArray[0] = DateTime.Parse($"{fiValueDateRangeArray[0]}-01").ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[0], @"^\d\d\d\d$")) fiValueDateRangeArray[0] = DateTime.Parse($"{fiValueDateRangeArray[0]}-01-01").ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[0], @"^\d\d\d\d[\-/]\d\d?[\-/]\d\d? \d\d?$")) fiValueDateRangeArray[0] = DateTime.Parse($"{fiValueDateRangeArray[0]}:00:00").ToString("yyyy-MM-dd HH:mm:ss");
                            else if (Regex.IsMatch(fiValueDateRangeArray[0], @"^\d\d\d\d[\-/]\d\d?[\-/]\d\d? \d\d?:\d\d?$")) fiValueDateRangeArray[0] = DateTime.Parse($"{fiValueDateRangeArray[0]}:00").ToString("yyyy-MM-dd HH:mm:ss");

                            exp = Expression.AndAlso(
                                Expression.GreaterThanOrEqual(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fiValueDateRangeArray[0]), exp.Type)),
                                Expression.LessThan(exp, Expression.Constant(Utils.GetDataReaderValue(exp.Type, fiValueDateRangeArray[1]), exp.Type)));
                            break;
                        case DynamicFilterOperator.Any:
                        case DynamicFilterOperator.NotAny:
                            var fiValueAnyArray = getFiListValue();
                            if (fiValueAnyArray.Length == 0) break;
                            var fiValueAnyArrayType = exp.Type.MakeArrayType();
                            exp = Expression.Call(GetMethodEnumerableContains(exp.Type), Expression.Constant(Utils.GetDataReaderValue(fiValueAnyArrayType, fiValueAnyArray), fiValueAnyArrayType), exp);
                            if (fi.Operator == DynamicFilterOperator.NotAny) exp = Expression.Not(exp);
                            break;
                    }

                    string[] getFiListValue()
                    {
                        if (fi.Value is string fiValueString) return fiValueString.Split(',');
                        if (fi.Value is IEnumerable fiValueIe)
                        {
                            var fiValueList = new List<string>();
                            foreach (var fiValueIeItem in fiValueIe)
                                fiValueList.Add(string.Concat(fiValueIeItem));
                            return fiValueList.ToArray();
                        }
                        return new string[0];
                    }

                    var sql = _commonExpression.ExpressionWhereLambda(_tables, exp, null, null, _params);

                    sb.Append(sql);
                }
                if (fi.Filters?.Any() == true)
                {
                    if (string.IsNullOrEmpty(fi.Field) == false)
                        sb.Append(" AND ");
                    if (fi.Logic == DynamicFilterLogic.Or) sb.Append("(");
                    for (var x = 0; x < fi.Filters.Count; x++)
                        ParseFilter(fi.Logic, fi.Filters[x], x == fi.Filters.Count - 1);
                    if (fi.Logic == DynamicFilterLogic.Or) sb.Append(")");
                }

                if (isend == false)
                {
                    if (string.IsNullOrEmpty(fi.Field) == false || fi.Filters?.Any() == true)
                    {
                        switch (logic)
                        {
                            case DynamicFilterLogic.And: sb.Append(" AND "); break;
                            case DynamicFilterLogic.Or: sb.Append(" OR "); break;
                        }
                    }
                }
            }
        }

        public TSelect DisableGlobalFilter(params string[] name)
        {
            if (_whereGlobalFilter.Any() == false) return this as TSelect;
            if (name?.Any() != true)
            {
                _whereCascadeExpression.RemoveRange(0, _whereGlobalFilter.Count);
                _whereGlobalFilter.Clear();
                return this as TSelect;
            }
            foreach (var n in name)
            {
                if (n == null) continue;
                var idx = _whereGlobalFilter.FindIndex(a => string.Compare(a.Name, n, true) == 0);
                if (idx == -1) continue;
                _whereCascadeExpression.RemoveAt(idx);
                _whereGlobalFilter.RemoveAt(idx);
            }
            return this as TSelect;
        }
        public TSelect ForUpdate(bool noawait = false)
        {
            if (_transaction == null && _orm.Ado.TransactionCurrentThread == null)
                throw new Exception("安全起见，请务必在事务开启之后，再使用 ForUpdate");
            switch (_orm.Ado.DataType)
            {
                case DataType.MySql:
                case DataType.OdbcMySql:
                    _tosqlAppendContent = " for update";
                    break;
                case DataType.SqlServer:
                case DataType.OdbcSqlServer:
                    _aliasRule = (_, old) => $"{old} With(UpdLock, RowLock{(noawait ? ", NoWait" : "")})";
                    break;
                case DataType.PostgreSQL:
                case DataType.OdbcPostgreSQL:
                case DataType.OdbcKingbaseES:
                    _tosqlAppendContent = $" for update{(noawait ? " nowait" : "")}";
                    break;
                case DataType.Oracle:
                case DataType.OdbcOracle:
                case DataType.Dameng:
                case DataType.OdbcDameng:
                    _tosqlAppendContent = $" for update{(noawait ? " nowait" : "")}";
                    break;
                case DataType.Sqlite:
                    break;
                case DataType.ShenTong: //神通测试中发现，不支持 nowait
                    _tosqlAppendContent = " for update";
                    break;
            }
            return this as TSelect;
        }
        #region common

        protected double InternalAvg(Expression exp)
        {
            var list = this.ToList<double>($"avg({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}");
            return list.Sum() / list.Count;
        }
        protected TMember InternalMax<TMember>(Expression exp) => this.ToList<TMember>($"max({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}").Max();
        protected TMember InternalMin<TMember>(Expression exp) => this.ToList<TMember>($"min({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}").Min();
        protected decimal InternalSum(Expression exp) => this.ToList<decimal>($"sum({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}").Sum();

        public ISelectGrouping<TKey, TValue> InternalGroupBy<TKey, TValue>(Expression columns)
        {
            var map = new ReadAnonymousTypeInfo();
            var field = new StringBuilder();
            var index = -10000; //临时规则，不返回 as1

            _commonExpression.ReadAnonymousField(_tables, field, map, ref index, columns, null, _whereCascadeExpression, false); //不走 DTO 映射
            var sql = field.ToString();
            this.GroupBy(sql.Length > 0 ? sql.Substring(2) : null);
            return new SelectGroupingProvider<TKey, TValue>(_orm, this, map, sql, _commonExpression, _tables);
        }
        public TSelect InternalJoin(Expression exp, SelectTableInfoType joinType)
        {
            _commonExpression.ExpressionJoinLambda(_tables, joinType, exp, null, _whereCascadeExpression);
            return this as TSelect;
        }
        protected TSelect InternalJoin<T2>(Expression exp, SelectTableInfoType joinType)
        {
            var tb = _commonUtils.GetTableByEntity(typeof(T2));
            if (tb == null) throw new ArgumentException("T2 类型错误");
            _tables.Add(new SelectTableInfo { Table = tb, Alias = $"IJ{_tables.Count}", On = null, Type = joinType });
            _commonExpression.ExpressionJoinLambda(_tables, joinType, exp, null, _whereCascadeExpression);
            return this as TSelect;
        }
        protected TSelect InternalOrderBy(Expression column) => this.OrderBy(_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, column, true, null));
        protected TSelect InternalOrderByDescending(Expression column) => this.OrderBy($"{_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, column, true, null)} DESC");

        public List<TReturn> InternalToList<TReturn>(Expression select) => this.ToListMapReader<TReturn>(this.GetExpressionField(select));
        protected string InternalToSql<TReturn>(Expression select, FieldAliasOptions fieldAlias = FieldAliasOptions.AsIndex)
        {
            var af = this.GetExpressionField(select, fieldAlias);
            return this.ToSql(af.field);
        }

        protected DataTable InternalToDataTable(Expression select)
        {
            var sql = this.InternalToSql<int>(select, FieldAliasOptions.AsProperty); //DataTable 使用 AsProperty
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            DataTable ret = null;
            Exception exception = null;
            try
            {
                ret = _orm.Ado.ExecuteDataTable(_connection, _transaction, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }

        protected TReturn InternalToAggregate<TReturn>(Expression select)
        {
            var map = new ReadAnonymousTypeInfo();
            var field = new StringBuilder();
            var index = 0;

            _commonExpression.ReadAnonymousField(_tables, field, map, ref index, select, null, _whereCascadeExpression, false); //不走 DTO 映射
            return this.ToListMapReader<TReturn>(new ReadAnonymousTypeAfInfo(map, field.Length > 0 ? field.Remove(0, 2).ToString() : null)).FirstOrDefault();
        }

        public TSelect InternalWhere(Expression exp) => exp == null ? this as TSelect : this.Where(_commonExpression.ExpressionWhereLambda(_tables, exp, null, _whereCascadeExpression, _params));
        #endregion

#if net40
#else
        async public Task<bool> AnyAsync()
        {
            this.Limit(1);
            return (await this.ToListAsync<int>($"1{_commonUtils.FieldAsAlias("as1")}")).Sum() > 0; //这里的 Sum 为了分表查询
        }

        async public Task<long> CountAsync()
        {
            var tmpOrderBy = _orderby;
            _orderby = null;
            try
            {
                return (await this.ToListAsync<int>($"count(1){_commonUtils.FieldAsAlias("as1")}")).Sum(); //这里的 Sum 为了分表查询
            }
            finally
            {
                _orderby = tmpOrderBy;
            }
        }

        async public Task<DataTable> ToDataTableAsync(string field = null)
        {
            var sql = this.ToSql(field);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            DataTable ret = null;
            Exception exception = null;
            try
            {
                ret = await _orm.Ado.ExecuteDataTableAsync(_connection, _transaction, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }

        async public Task<List<TTuple>> ToListAsync<TTuple>(string field)
        {
            var sql = this.ToSql(field);
            var type = typeof(TTuple);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<TTuple>();
            var flagStr = $"ToListField:{field}";
            Exception exception = null;
            try
            {
                await _orm.Ado.ExecuteReaderAsync(_connection, _transaction, fetch =>
                {
                    var read = Utils.ExecuteArrayRowReadClassOrTuple(flagStr, type, null, fetch.Object, 0, _commonUtils);
                    ret.Add((TTuple)read.Value);
                    return Task.FromResult(false);
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }

        async internal Task<List<T1>> ToListAfPrivateAsync(string sql, GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<T1>();
            Exception exception = null;
            try
            {
                await _orm.Ado.ExecuteReaderAsync(_connection, _transaction, fetch =>
                {
                    ret.Add(af.Read(_orm, fetch.Object));
                    if (otherData != null)
                    {
                        var idx = af.FieldCount - 1;
                        foreach (var other in otherData)
                            other.retlist.Add(_commonExpression.ReadAnonymous(other.read, fetch.Object, ref idx, false, null));
                    }
                    return Task.FromResult(false);
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            foreach (var include in _includeToListAsync) await include?.Invoke(ret);
            _trackToList?.Invoke(ret);
            return ret;
        }

        internal Task<List<T1>> ToListPrivateAsync(GetAllFieldExpressionTreeInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            string sql = null;
            if (otherData?.Length > 0)
            {
                var sbField = new StringBuilder().Append(af.Field);
                foreach (var other in otherData)
                    sbField.Append(other.field);
                sql = this.ToSql(sbField.ToString());
            }
            else
                sql = this.ToSql(af.Field);

            return ToListAfPrivateAsync(sql, af, otherData);
        }

        public Task<Dictionary<TKey, T1>> ToDictionaryAsync<TKey>(Func<T1, TKey> keySelector) => ToDictionaryAsync(keySelector, a => a);
        async public Task<Dictionary<TKey, TElement>> ToDictionaryAsync<TKey, TElement>(Func<T1, TKey> keySelector, Func<T1, TElement> elementSelector)
        {
            if (keySelector == null) throw new ArgumentNullException(nameof(keySelector));
            if (elementSelector == null) throw new ArgumentNullException(nameof(elementSelector));
            var af = this.GetAllFieldExpressionTreeLevel2();
            var sql = this.ToSql(af.Field);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new Dictionary<TKey, TElement>();
            Exception exception = null;
            try
            {
                await _orm.Ado.ExecuteReaderAsync(_connection, _transaction, fetch =>
                {
                    var item = af.Read(_orm, fetch.Object);
                    ret.Add(keySelector(item), elementSelector(item));
                    return Task.FromResult(false);
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            if (typeof(TElement) == typeof(T1)) _trackToList?.Invoke(ret.Values);
            return ret;
        }
        public virtual Task<List<T1>> ToListAsync(bool includeNestedMembers = false)
        {
            if (_selectExpression != null) return this.InternalToListAsync<T1>(_selectExpression);
            return this.ToListPrivateAsync(includeNestedMembers == false ? this.GetAllFieldExpressionTreeLevel2() : this.GetAllFieldExpressionTreeLevelAll(), null);
        }

        async public Task<T1> ToOneAsync()
        {
            this.Limit(1);
            return (await this.ToListAsync()).FirstOrDefault();
        }

        public Task<T1> FirstAsync() => this.ToOneAsync();

        async internal Task<List<TReturn>> ToListMrPrivateAsync<TReturn>(string sql, ReadAnonymousTypeAfInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            var type = typeof(TReturn);
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            var ret = new List<TReturn>();
            Exception exception = null;
            try
            {
                await _orm.Ado.ExecuteReaderAsync(_connection, _transaction, fetch =>
                {
                    var index = -1;
                    ret.Add((TReturn)_commonExpression.ReadAnonymous(af.map, fetch.Object, ref index, false, null));
                    if (otherData != null)
                        foreach (var other in otherData)
                            other.retlist.Add(_commonExpression.ReadAnonymous(other.read, fetch.Object, ref index, false, null));
                    return Task.FromResult(false);
                }, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            if (typeof(TReturn) == typeof(T1))
                foreach (var include in _includeToListAsync) await include?.Invoke(ret);
            _trackToList?.Invoke(ret);
            return ret;
        }
        internal Task<List<TReturn>> ToListMapReaderPrivateAsync<TReturn>(ReadAnonymousTypeAfInfo af, ReadAnonymousTypeOtherInfo[] otherData)
        {
            string sql = null;
            if (otherData?.Length > 0)
            {
                var sbField = new StringBuilder().Append(af.field);
                foreach (var other in otherData)
                    sbField.Append(other.field);
                sql = this.ToSql(sbField.ToString());
            }
            else
                sql = this.ToSql(af.field);

            return ToListMrPrivateAsync<TReturn>(sql, af, otherData);
        }
        protected Task<List<TReturn>> ToListMapReaderAsync<TReturn>(ReadAnonymousTypeAfInfo af) => ToListMapReaderPrivateAsync<TReturn>(af, null);

        async protected Task<double> InternalAvgAsync(Expression exp)
        {
            var list = await this.ToListAsync<double>($"avg({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}");
            return list.Sum() / list.Count;
        }
        async protected Task<TMember> InternalMaxAsync<TMember>(Expression exp) => (await this.ToListAsync<TMember>($"max({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}")).Max();
        async protected Task<TMember> InternalMinAsync<TMember>(Expression exp) => (await this.ToListAsync<TMember>($"min({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}")).Min();
        async protected Task<decimal> InternalSumAsync(Expression exp) => (await this.ToListAsync<decimal>($"sum({_commonExpression.ExpressionSelectColumn_MemberAccess(_tables, null, SelectTableInfoType.From, exp, true, null)}){_commonUtils.FieldAsAlias("as1")}")).Sum();

        protected Task<List<TReturn>> InternalToListAsync<TReturn>(Expression select) => this.ToListMapReaderAsync<TReturn>(this.GetExpressionField(select));

        async protected Task<DataTable> InternalToDataTableAsync(Expression select)
        {
            var sql = this.InternalToSql<int>(select, FieldAliasOptions.AsProperty); //DataTable 使用 AsProperty
            var dbParms = _params.ToArray();
            var before = new Aop.CurdBeforeEventArgs(_tables[0].Table.Type, _tables[0].Table, Aop.CurdType.Select, sql, dbParms);
            _orm.Aop.CurdBeforeHandler?.Invoke(this, before);
            DataTable ret = null;
            Exception exception = null;
            try
            {
                ret = await _orm.Ado.ExecuteDataTableAsync(_connection, _transaction, CommandType.Text, sql, dbParms);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw ex;
            }
            finally
            {
                var after = new Aop.CurdAfterEventArgs(before, exception, ret);
                _orm.Aop.CurdAfterHandler?.Invoke(this, after);
            }
            return ret;
        }

        async protected Task<TReturn> InternalToAggregateAsync<TReturn>(Expression select)
        {
            var map = new ReadAnonymousTypeInfo();
            var field = new StringBuilder();
            var index = 0;

            _commonExpression.ReadAnonymousField(_tables, field, map, ref index, select, null, _whereCascadeExpression, false); //不走 DTO 映射
            return (await this.ToListMapReaderAsync<TReturn>(new ReadAnonymousTypeAfInfo(map, field.Length > 0 ? field.Remove(0, 2).ToString() : null))).FirstOrDefault();
        }
#endif
    }
}