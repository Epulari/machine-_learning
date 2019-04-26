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
    }
}
