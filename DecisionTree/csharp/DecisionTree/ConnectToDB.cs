using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Npgsql;
using System.Data;
using System.Text.RegularExpressions;

namespace DecisionTree
{
    class ConnectToDB
    {
        /// <summary>
        /// 连接数据库
        /// </summary>
        NpgsqlConnection conn;
        NpgsqlCommand cmd;
        private readonly object lockexecute1 = new object();
        private readonly object lockexecute2 = new object();
        private readonly string classname;

        public ConnectToDB(string database)
        {
            string databaseaddress = $"Host=localhost;Port=5432;Username=postgres;Password=282794293;Database={database}";
            conn = new NpgsqlConnection(databaseaddress);
            conn.Open();
            cmd = new NpgsqlCommand();
            classname = "classification";
        }

        /// <summary>
        /// 关闭数据库
        /// </summary>
        public void CloseDB()
        {
            conn.Close();
        }

        /// <summary>
        /// 执行命令，没有返回值
        /// </summary>
        /// <param name="command"></param>
        private void ExecuteWithoutReturn(string command)
        {
            lock (lockexecute1)
            {
                cmd.Connection = conn;
                cmd.CommandTimeout = 3600;
                cmd.CommandText = command;
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(ex);
                    Console.ForegroundColor = ConsoleColor.Gray;
                    return;
                }
                finally
                {
                    cmd.Parameters.Clear();
                }
            }
        }

        /// <summary>
        /// 执行命令，有返回值
        /// </summary>
        /// <param name="command"></param>
        /// <returns>执行命令的结果</returns>
        private DataTable ExecuteAndReturn(string command)
        {
            lock (lockexecute2)
            {
                cmd.Connection = conn;
                cmd.CommandTimeout = 3600;
                cmd.CommandText = command;
                NpgsqlDataAdapter da = new NpgsqlDataAdapter(cmd);
                DataSet ds = new DataSet();
                da.Fill(ds, "ds");
                cmd.Parameters.Clear();
                return ds.Tables[0];
            }
        }

        /// <summary>
        /// Program：断开数据库的所有连接
        /// </summary>
        /// <param name="dbname">数据库名</param>
        public void DisconnectDB(string dbname)
        {
            ExecuteWithoutReturn($"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE datname='{dbname}' AND pid<>pg_backend_pid();");
        }

        /// <summary>
        /// Program：获取数据库名称
        /// </summary>
        public DataTable GetDBname()
        {
            return ExecuteAndReturn($"SELECT * FROM pg_database WHERE datacl is null AND datname != 'postgres'");
        }

        /// <summary>
        /// RandomForest / ClearGarbage：删除数据库
        /// </summary>
        /// <param name="dbname">数据库名</param>
        public void DeleteDB(string dbname)
        {
            ExecuteWithoutReturn($"DROP DATABASE IF EXISTS {dbname};");
        }

        /// <summary>
        /// RandomForest / ClearGarbage：删除表
        /// </summary>
        /// <param name="tbname">表名</param>
        public void DeleteTable(string tbname)
        {
            ExecuteWithoutReturn($"DROP TABLE {tbname};");
        }

        /// <summary>
        /// GlobalVariable：根据条件获取数据库中的表名
        /// </summary>
        /// <returns>表名集合</returns>
        public string[] GetTableName()
        {
            DataTable dt = ExecuteAndReturn($"SELECT relname FROM pg_class c WHERE relkind = 'r' AND relname not like 'pg_%' AND relname not like 'sql_%' AND LENGTH(relname) < 20;");
            int len = dt.Rows.Count;
            string[] tables = new string[len];
            for (int i = 0; i < len; i++)
            {
                tables[i] = dt.Rows[i][0].ToString();
            }
            return tables;
        }

        /// <summary>
        /// ClearGarbage：根据条件获取数据库中的表名
        /// </summary>
        /// <param name="tbname">表名中含有的字段</param>
        /// <returns>表名</returns>
        public string[] GetTableName(string tbname)
        {
            DataTable dt = ExecuteAndReturn($"SELECT relname FROM pg_class c WHERE relkind = 'r' AND relname not like 'pg_%' AND relname not like 'sql_%' AND relname like '{tbname}%';");
            int len = dt.Rows.Count;
            string[] tables = new string[len];
            for (int i = 0; i < len; i++)
            {
                tables[i] = dt.Rows[i][0].ToString();
            }
            return tables;
        }

        /// <summary>
        /// SpatialDecisionTree：根据父表创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrresult">子表中的字段</param>
        /// <param name="attrcondition">条件字段名</param>
        /// <param name="conditionvalue">条件字段值</param>
        public void CreateChildTable(string fathertb, string childtb, string attrresult, string attrcondition, List<int> conditionvalue)
        {
            StringBuilder str = new StringBuilder();
            str.Append($"CREATE TABLE {childtb} AS (SELECT {attrresult} FROM {fathertb} WHERE {attrcondition} = {conditionvalue[0].ToString()}");
            for (int i = 1, len = conditionvalue.Count; i < len; i++)
            {
                str.Append($" OR {attrcondition} = {conditionvalue[i].ToString()}");
            }
            str.Append($");");
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// DataPreprocess：根据父表创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrresult">要添加到子表中的字段</param>
        /// <param name="primarykey">主键字段</param>
        public void CreateChildTable(string fathertb, string childtb, string attrresult, string primarykey)
        {
            StringBuilder str = new StringBuilder();
            str.Append($"CREATE TABLE {childtb} AS (SELECT {attrresult} FROM {fathertb});"); // 创建随机森林数据表，所有属性全部放在里面
            str.Append($"ALTER TABLE {childtb} ADD PRIMARY KEY({primarykey});");
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// DataPreprocess：根据父表创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrAndType">子表字段和类型</param>
        /// <param name="attrresult">子表被插入值的字段</param>
        /// <param name="attrselect">子表被插入值的字段在父表中对应的字段</param>
        public void CreateChildTable(string fathertb, string childtb, string attrAndType, string attrresult, string attrselect)
        {
            StringBuilder str = new StringBuilder();
            str.Append($"CREATE TABLE {childtb}({attrAndType});"); // 创建随机森林数据表，所有属性全部放在里面
            str.Append($"INSERT INTO {childtb}({attrresult}) SELECT {attrselect} FROM {fathertb};");
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// DataPreprocess：根据父表和一次性条件创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrAndType">子表字段和类型</param>
        /// <param name="attrresult">子表被插入值的字段</param>
        /// <param name="attrselect">子表被插入值的字段在父表中对应的字段</param>
        /// <param name="attrcondition">子表被插入值的字段在父表中对应的字段</param>
        public void CreateChildTable(string fathertb, string childtb, string attrAndType, string attrresult, string attrselect, string attrcondition)
        {
            StringBuilder str = new StringBuilder();
            str.Append($"CREATE TABLE {childtb}({attrAndType});"); // 创建随机森林数据表，所有属性全部放在里面
            str.Append($"INSERT INTO {childtb}({attrresult}) SELECT {attrselect} FROM {fathertb} WHERE {attrcondition};");
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// RandomForest：根据父表和批量条件创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrAndType">子表字段和类型</param>
        /// <param name="attrresult">子表被插入值的字段</param>
        /// <param name="attrselect">子表被插入值的字段在父表中对应的字段</param>
        /// <param name="attrcondition">父表中条件字段</param>
        /// <param name="conditionvalue">父表中条件字段对应的值</param>
        public void CreateChildTable(string fathertb, string childtb, string attrAndType, string attrresult, string attrselect, string attrcondition, string[] conditionvalue)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"CREATE TABLE {childtb}({attrAndType});");
            int count = 0;
            foreach (string value in conditionvalue)
            {
                if (count % 1000 == 0)
                {
                    ExecuteWithoutReturn(sb.ToString());
                    sb.Clear();
                    sb.Append($"INSERT INTO {childtb}({attrresult}) SELECT {attrselect} FROM {fathertb} WHERE {attrcondition} = {value}");
                }
                else
                {
                    sb.Append($" OR {attrcondition} = {value}");
                }
                count++;
            }
            if (sb.Length != 0)
                ExecuteWithoutReturn(sb.ToString());
        }

        /// <summary>
        /// ClassicDecisionTree：根据父表、最优特征、特征值创建子表
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="feature">没有最优特征的特征集</param>
        /// <param name="bestfeature">最优特征</param>
        /// <param name="value">最优特征中的一个属性值</param>
        /// <returns>子表名称</returns>
        public string CreateChildTable(string fathertb, List<string> feature, string bestfeature, int value)
        {
            string childtb = fathertb + bestfeature + value + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            int len = childtb.Length;
            if (len > 60)
            {
                string str = Regex.Replace(childtb, "[0-9]", "", RegexOptions.IgnoreCase).Substring(0, 2); // 截取父表名称中的前两个字母
                childtb = bestfeature.Substring(0, 2) + str + childtb.Substring(len - 60, 55);
            }
            string createChildtb = $"CREATE TABLE {childtb} as (SELECT {string.Join(", ", feature)}, {classname} FROM {fathertb} WHERE {bestfeature} = {value});";
            ExecuteWithoutReturn(createChildtb);
            return childtb;
        }

        /// <summary>
        /// DataPreprocess：增加字段(列)
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="columnname">字段名</param>
        /// <param name="columntype">字段类型</param>
        public void AddColumn(string tbname, string columnname, string columntype)
        {
            ExecuteWithoutReturn($"ALTER TABLE {tbname} ADD {columnname} {columntype};");
        }

        /// <summary>
        /// RandomData / ClassicDecisionTree：获取表的字段名
        /// </summary>
        /// <param name="tbname">表名</param>
        public string[] GetAttributeName(string tbname)
        {
            DataTable dt = ExecuteAndReturn($"SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND table_name='{tbname}';");
            int attrNum = dt.Rows.Count;
            string[] attrName = new string[attrNum];
            for (int i = 0; i < attrNum; i++)
            {
                attrName[i] = dt.Rows[i][0].ToString();
            }
            return attrName;
        }

        /// <summary>
        /// GlobalVariable：根据条件获取数据数量
        /// </summary>
        /// <param name="tbname">表名</param>
        public int GetDataNum(string tbname, string condition)
        {
            DataTable dt = ExecuteAndReturn($"SELECT COUNT(*) FROM {tbname} WHERE {condition};");
            return int.Parse(dt.Rows[0][0].ToString());
        }

        /// <summary>
        /// DataPreprocess：无条件地获取数据
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要查找的数据的字段名</param>
        /// <returns>结果表</returns>
        public DataTable GetData(string tbname, string attrresult)
        {
            return ExecuteAndReturn($"SELECT {attrresult} FROM {tbname};");
        }

        /// <summary>
        /// DataPreprocess：有条件地获取数据
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要查找的数据的字段名</param>
        /// <param name="condition">条件语句</param>
        /// <returns>结果表</returns>
        public DataTable GetData(string tbname, string attrresult, string condition)
        {
            return ExecuteAndReturn($"SELECT {attrresult} FROM {tbname} WHERE {condition};");
        }

        /// <summary>
        /// DataPreprocess：获取字段的最大值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attr">求最大值的字段</param>
        /// <returns>最大值</returns>
        public int GetMaxValue(string tbname, string attr)
        {
            string maxValue = $"SELECT MAX({attr}) FROM {tbname};";
            DataTable dt = ExecuteAndReturn(maxValue);
            return int.Parse(dt.Rows[0][0].ToString());
        }

        /// <summary>
        /// DataPreprocess：根据条件获取字段的最大最小值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要获取最大最小值的字段</param>
        /// <param name="condition">条件字段</param>
        /// <param name="conditionvalue">条件值</param>
        /// <returns>最大最小值组成的一维数组</returns>
        public int[] GetMaxMinValue(string tbname, string attrresult, string condition)
        {
            int[] maxmin = new int[2];
            DataTable dt = ExecuteAndReturn($"SELECT MAX({attrresult}), MIN({attrresult}) FROM {tbname}  WHERE {condition};");
            if (dt == null)
            {
                return null;
            }
            maxmin[0] = int.Parse(dt.Rows[0][0].ToString());
            maxmin[1] = int.Parse(dt.Rows[0][1].ToString());
            return maxmin;
        }

        /// <summary>
        /// DataPreprocess：获取字段的所有值，且值唯一
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="disattr">唯一值的字段名</param>
        /// <param name="attr">要显示的内容的字段名</param>
        /// <returns>属性或类别值</returns>
        public DataTable GetUniqueValue(string tbname, string disattr, string attr)
        {
            string valueInRow = $"SELECT DISTINCT ON ({disattr}) {attr} FROM {tbname};"; // 去重
            return ExecuteAndReturn(valueInRow);
        }

        /// <summary>
        /// DataPreprocess / RandomForest：根据条件获取字段的所有值，且值唯一
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="disattr">唯一值的字段名</param>
        /// <param name="attr">要显示的内容的字段名</param>
        /// <param name="condition">条件</param>
        /// <returns>属性或类别值</returns>
        public DataTable GetUniqueValue(string tbname, string disattr, string attr, string condition)
        {
            string valueInRow = $"SELECT DISTINCT ON ({disattr}) {attr} FROM {tbname} Where {condition};"; // 去重
            return ExecuteAndReturn(valueInRow);
        }

        /// <summary>
        /// ClassicDecisionTree：获取字段的所有值及每个值的个数
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attr">字段名</param>
        /// <returns>字段值及对应个数</returns>
        public int[][] GetUniqueValueAndNum(string tbname, string attr)
        {
            DataTable valueInRow = GetUniqueValue(tbname, attr, attr), dt;
            int len = valueInRow.Rows.Count, value;
            int[][] valueCount = new int[][] { new int[len], new int[len] };
            for (int i = 0; i < len; i++)
            {
                value = int.Parse(valueInRow.Rows[i][0].ToString());
                dt = ExecuteAndReturn($"SELECT COUNT(*) FROM {tbname} WHERE {attr} = {value};");
                valueCount[0][i] = value; // 数组1，值
                valueCount[1][i] = int.Parse(dt.Rows[0][0].ToString()); // 数组2，值对应的个数
            }
            return valueCount;
        }

        /// <summary>
        /// ClassicDecisionTree：在属性值确定的条件下获取类别的名称及每个类别的个数
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="feature">特征因子</param>
        /// <param name="value">属性值</param>
        /// <returns>类别值及个数</returns>
        public int[][] GetUniqueValueAndNum(string tbname, string feature, int value)
        {
            string valueInRow = $"SELECT DISTINCT ON ({classname}) {classname} FROM {tbname} WHERE {feature} = {value};"; // 去重
            DataTable dt = ExecuteAndReturn(valueInRow), dt2;
            int len = dt.Rows.Count, data;
            int[][] valueCount = new int[][] { new int[len], new int[len] };
            for (int i = 0; i < len; i++)
            {
                data = int.Parse(dt.Rows[i][0].ToString());
                valueInRow = $"SELECT COUNT(*) FROM {tbname} WHERE {feature} = {value} AND {classname} = {data};";
                dt2 = ExecuteAndReturn(valueInRow);
                valueCount[0][i] = data; // 数组1，值
                valueCount[1][i] = int.Parse(dt2.Rows[0][0].ToString()); // 数组2，值对应的个数
            }
            return valueCount;
        }

        /// <summary>
        /// ClassicDecisionTree：获取属性的类别，同一类
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="feature">特征因子</param>
        /// <param name="value">属性值</param>
        /// <returns>类别值</returns>
        public string GetClassOfSameValueSameClass(string tbname, string feature, int value)
        {
            string valueClass = $"SELECT {classname} FROM {tbname} WHERE {feature} = {value} ORDER BY {classname} LIMIT 1;";
            DataTable dt = ExecuteAndReturn(valueClass);
            return dt.Rows[0][0].ToString();
        }

        /// <summary>
        /// ClassicDecisionTree：比较 属性对应的某一条数据的类别在该属性的总个数 与 该属性的总个数，判断是否是同一类别   
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="feature">特征因子</param>
        /// <param name="value">属性值</param>
        /// <returns>true/false</returns>
        public bool IsOneFeatureOneClass(string tbname, string feature, int value)
        {
            string oneDataInColumnNum = $@"CREATE OR REPLACE FUNCTION getnum
                (OUT onedatanum INTEGER, OUT columnnum INTEGER)
                AS $$
                BEGIN
                    onedatanum := (SELECT COUNT(*) FROM {tbname} WHERE {classname} = (SELECT {classname} FROM {tbname} WHERE {feature} = {value} ORDER BY {classname} LIMIT 1) 
                                    AND {feature} = {value});
                    columnnum := (SELECT COUNT(*) FROM {tbname} WHERE {feature} = {value});
                END;
                $$ LANGUAGE PLPGSQL;

                SELECT * FROM getnum();";
            DataTable dt = ExecuteAndReturn(oneDataInColumnNum);
            return dt.Rows[0][0].ToString() == dt.Rows[0][1].ToString();
        }

        /// <summary>
        /// DataPreprocess：根据条件更新字段值，多个条件对应一个值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要更新的字段名</param>
        /// <param name="attrcondition">条件的字段名</param>
        /// <param name="value">字段值及对应条件的值(id)</param>
        public void UpdateData(string tbname, string attrresult, string attrcondition, Dictionary<string, List<string>> value)
        {
            List<StringBuilder> insertValue = new List<StringBuilder>(3700);
            StringBuilder str = new StringBuilder(90);
            foreach (var item in value)
            {
                for (int i = 0, len = item.Value.Count; i < len; i++)
                {
                    if (i % 1000 == 0)
                    {
                        str = new StringBuilder(90);
                        insertValue.Add(str);
                        str.Append($"UPDATE {tbname} SET {attrresult} = {item.Key} WHERE {attrcondition} = {item.Value[i]}");
                    }
                    else
                    {
                        str.Append($" OR {attrcondition} = {item.Value[i]}");
                    }
                }
            }
            value = null;
            GC.Collect();
            foreach (StringBuilder sb in insertValue)
            {
                ExecuteWithoutReturn(sb.ToString());
            }
        }

        /// <summary>
        /// DataPreprocess：根据条件更新字段值，多个条件对应一个值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要更新的字段名</param>
        /// <param name="value">字段值及对应条件</param>
        public void UpdateData(string tbname, string attrresult, Dictionary<string, List<string>> value)
        {
            List<StringBuilder> insertValue = new List<StringBuilder>(3700);
            StringBuilder str = new StringBuilder(90);
            foreach (var item in value)
            {
                for (int i = 0, len = item.Value.Count; i < len; i++)
                {
                    if (i % 1000 == 0)
                    {
                        str = new StringBuilder(90);
                        insertValue.Add(str);
                        str.Append($"UPDATE {tbname} SET {attrresult} = {item.Key} WHERE {item.Value[i]}");
                    }
                    else
                    {
                        str.Append($" OR {item.Value[i]}");
                    }
                }
            }
            value = null;
            GC.Collect();
            foreach (StringBuilder sb in insertValue)
            {
                ExecuteWithoutReturn(sb.ToString());
            }
        }

        /// <summary>
        /// DataPreprocess：将连续值设置为离散值，从0开始依次递增
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">离散值的字段名</param>
        /// <param name="attrcondition">连续值的字段名</param>
        /// <param name="conditionvalue">连续值的节点数组</param>
        public void UpdateData(string tbname, string attrresult, string attrcondition, string[] conditionvalue)
        {
            StringBuilder str = new StringBuilder(100);
            int len = conditionvalue.Length;
            if (len <= 2)
            {
                // 所有最大最小值相同，则所有值都相同
                str.Append($"UPDATE {tbname} SET {attrresult} = 0;");
            }
            else
            {
                str.Append($"UPDATE {tbname} SET {attrresult} = 0 WHERE {attrcondition} <= {conditionvalue[1]};");

                for (int i = 2; i < len; i++)
                {
                    str.Append($"UPDATE {tbname} SET {attrresult} = {(i - 1).ToString()} WHERE {attrcondition} > {conditionvalue[i - 1]} AND {attrcondition} <= {conditionvalue[i]};");
                }
            }
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// DataPreprocess：根据父表与子表相同列更新子表值
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrresult">要更新的字段</param>
        /// <param name="attrselect">被选择的字段</param>
        /// <param name="attrcondition">相同字段</param>
        public void UpdateData(string fathertb, string childtb, string attrresult, string attrselect, string attrcondition)
        {
            ExecuteWithoutReturn($"EXPLAIN (ANALYZE,VERBOSE,TIMING,BUFFERS) UPDATE {childtb} SET {attrresult}={fathertb}.{attrselect} FROM {fathertb} WHERE {childtb}.{attrcondition}={fathertb}.{attrcondition};");
        }

        /// <summary>
        /// DataPreprocess：根据父表与子表相同列更新子表值
        /// </summary>
        /// <param name="fathertb">父表</param>
        /// <param name="childtb">子表</param>
        /// <param name="attrresult">要更新的字段</param>
        /// <param name="attrselect">被选择的字段</param>
        /// <param name="attrcondition">相同字段</param>
        public void UpdateData(string fathertb, string childtb, string[] attrresult, string[] attrselect, string attrcondition)
        {
            StringBuilder str = new StringBuilder(100);
            str.Append($"EXPLAIN (ANALYZE,VERBOSE,TIMING,BUFFERS) UPDATE {childtb} SET ");
            str.Append($"{attrresult[0]}={fathertb}.{attrselect[0]}");
            for (int i = 1, len = attrresult.Length; i < len; i++)
            {
                str.Append($", {attrresult[i]}={fathertb}.{attrselect[i]}");
            }
            str.Append($" FROM {fathertb} WHERE {childtb}.{attrcondition}={fathertb}.{attrcondition};");
            ExecuteWithoutReturn(str.ToString());
        }

        /// <summary>
        /// 更新值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要更新的字段</param>
        /// <param name="resultvalue">要更新的值</param>
        /// <param name="condition">条件</param>
        public void UpdateData(string tbname, string attrresult, string resultvalue)
        {
            ExecuteWithoutReturn($"UPDATE {tbname} SET {attrresult} = {resultvalue};");
        }

        /// <summary>
        /// 根据条件更新值
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <param name="attrresult">要更新的字段</param>
        /// <param name="resultvalue">要更新的值</param>
        /// <param name="condition">条件</param>
        public void UpdateData(string tbname, string attrresult, string resultvalue, string condition)
        {
            ExecuteWithoutReturn($"UPDATE {tbname} SET {attrresult} = {resultvalue} Where {condition};");
        }
    }
}
