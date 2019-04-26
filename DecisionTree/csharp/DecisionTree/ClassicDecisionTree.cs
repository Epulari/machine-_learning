using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DecisionTree
{
    public struct Tree
    {
        public string data;
        public int[] children;
        public Tree[] offspring;
    }
    /// <summary>
    /// 经典决策树 C4.5算法
    /// </summary>
    class ClassicDecisionTree
    {
        private readonly ConnectToDB connectdb; // 连接数据库的实例
        private readonly string classname; // 类别的字段名

        /// <summary>
        /// 构造函数：一组特征直接得到结果
        /// </summary>
        /// <param name="treename">决策树的表所在的数据库名</param>
        /// <param name="classname">类别的字段名</param>
        public ClassicDecisionTree(string treename, string classname)
        {
            connectdb = new ConnectToDB(treename);
            this.classname = classname;
        }

        /// <summary>
        /// 根据特征创建树：一定有结果
        /// </summary>
        /// <param name="roottbname">当前树的根表</param>
        /// <param name="tbname">当前树（分支）的数据表，要注意feature的使用，链表为引用传递，会改变原数据</param>
        /// <param name="closedb">函数回到最外层时是否关闭数据库，若是多个特征组调用该方法需要设置为false</param>
        /// <returns>树</returns>
        public Tree CreateTree(string roottbname, string tbname, List<string> feature, bool closedb)
        {
            try
            {
                Tree tree;
                tree.data = null;
                tree.children = null;
                tree.offspring = new Tree[1];

                string bestFeature = null;
                int[] bestFeatureValue = null;

            // 只剩一个特征时，该特征即为最优特征
            nexttree: int featureLen = feature.Count;
                if (featureLen == 1)
                {
                    bestFeature = feature[0];
                    bestFeatureValue = connectdb.GetUniqueValueAndNum(tbname, bestFeature)[0];
                }
                else
                {
                    List<int> bestFeatureData = ChooseBestFeature(tbname, feature);
                    bestFeature = feature[bestFeatureData.Last()];
                    bestFeatureData.RemoveAt(bestFeatureData.Count - 1);
                    bestFeatureValue = bestFeatureData.ToArray();
                }
                tree.data = bestFeature;
                tree.children = bestFeatureValue;
                tree.offspring = new Tree[bestFeatureValue.Max() + 1]; // 将每个属性对应的子树放入属性值对应的下标，则按特征的属性的最大值定义孩子的最大索引，因此长度+1
                
                if ((featureLen == 1) && (bestFeatureValue.Length == 1))
                {
                    // 是最后一个特征且只有一个特征值，直接本次的树只有一个值，作为上一棵树的分支
                    int max, maxIndex; // 最多类别值，对应的索引
                    int[][] classCount; // 类别值及个数
                    // 根据特征的属性值分类
                    // 获取该属性对应的所有类别及类别个数，判断是否是同一类别
                    classCount = connectdb.GetUniqueValueAndNum(tbname, bestFeature, bestFeatureValue[0]);
                    if (classCount[0].Length == 1)
                    {
                        maxIndex = 0;
                    }
                    else
                    {
                        max = classCount[1].Max();
                        maxIndex = classCount[1].ToList().IndexOf(max);
                    }

                    tree.data = classCount[0][maxIndex].ToString();
                    tree.children = null;
                    tree.offspring = new Tree[1];
                }
                else if (featureLen == 1)
                {
                    // 最后一个特征，有多个特征值，本次树为树，有多个分支
                    int max, maxIndex; // 最多类别值，对应的索引
                    int[][] classCount; // 类别值及个数
                    // 根据特征的属性值分类
                    foreach (int value in bestFeatureValue)
                    {
                        // 获取该属性对应的所有类别及类别个数，判断是否是同一类别
                        classCount = connectdb.GetUniqueValueAndNum(tbname, bestFeature, value);
                        if (classCount[0].Length == 1)
                        {
                            maxIndex = 0;
                        }
                        else
                        {
                            max = classCount[1].Max();
                            maxIndex = classCount[1].ToList().IndexOf(max);
                        }

                        Tree childTree;
                        childTree.data = classCount[0][maxIndex].ToString();
                        childTree.children = null;
                        childTree.offspring = new Tree[1];

                        tree.offspring[value] = childTree;
                    }
                }
                else if (bestFeatureValue.Length == 1)
                {
                    // 如果不是最后一个特征，而特征值只有一个，则直接向后建立分支
                    feature.Remove(bestFeature);
                    goto nexttree;
                }
                else
                {
                    // 从特征集中移除最优特征
                    feature.Remove(bestFeature);
                    // 根据特征的属性值继续分类
                    foreach (int value in bestFeatureValue)
                    {
                        // 判断属性值是否属于同一类别
                        if (connectdb.IsOneFeatureOneClass(tbname, bestFeature, value))
                        {
                            Tree childTree;
                            childTree.data = connectdb.GetClassOfSameValueSameClass(tbname, bestFeature, value);
                            childTree.children = null;
                            childTree.offspring = new Tree[1];

                            tree.offspring[value] = childTree;
                        }
                        else
                        {
                            tree.offspring[value] = CreateTree(roottbname, connectdb.CreateChildTable(tbname, feature, bestFeature, value), feature.ToList(), closedb); // 回调
                        }
                    }
                }

                // 回调回到最外层
                if (closedb && (tbname == roottbname))
                {
                    connectdb.CloseDB();
                }
                return tree;
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        /// <summary>
        /// 选择最优特征
        /// </summary>
        /// <param name="tbname">表名</param>
        /// <returns>最优特征对应的属性值+最优特征的索引</returns>
        private List<int> ChooseBestFeature(string tbname, List<string> feature)
        {
            // 数据集的经验熵
            int[][] classCount = connectdb.GetUniqueValueAndNum(tbname, classname);
            decimal sumNum = classCount[1].Sum();
            decimal baseEntropy = CalculateEntropy(classCount, sumNum);

            int[][] valueCount;
            decimal valueEntropy, featureEntropy, valueNum, prob, featureInfoGainRatio, bestInfoGainRatio = 0;
            int[] bestFeatureValue = null;
            int bestFeatureIndex = 0, index = 0;
            // 计算每个特征因子对数据集的经验条件熵
            foreach (string feat in feature)
            {
                valueCount = connectdb.GetUniqueValueAndNum(tbname, feat); // 获取特征因子的每个属性值及其个数, valueCount[1].Sum()=classCount[1].Sum()
                int lenValue = valueCount[0].Length;

                // 特征值个数等于1，featureEntropy为0，分母无穷小，分子无穷大
                if (lenValue == 1)
                {
                    bestFeatureIndex = index;
                    bestFeatureValue = valueCount[0];
                    break;
                }

                valueEntropy = 0;
                featureEntropy = 0;
                // 特征值个数大于1
                for (int i = 0; i < lenValue; i++)
                {
                    valueNum = valueCount[1][i];
                    prob = valueNum / sumNum;
                    classCount = connectdb.GetUniqueValueAndNum(tbname, feat, valueCount[0][i]);
                    valueEntropy += prob * CalculateEntropy(classCount, valueNum);
                    featureEntropy -= prob * Convert.ToDecimal(Math.Log(Convert.ToDouble(prob), 2));
                }
                featureInfoGainRatio = (baseEntropy - valueEntropy) / featureEntropy;
                // 最优特征
                if (featureInfoGainRatio > bestInfoGainRatio)
                {
                    bestInfoGainRatio = featureInfoGainRatio;
                    bestFeatureIndex = index;
                    bestFeatureValue = valueCount[0];
                }
                index++;
            }

            List<int> bestFeatureData;
            // 所有特征的信息增益为0，则信息增益比也都为0，代表特征不能减少类别不确定信，类别在总数中占比相同，在任意一个特征值中占比也相同
            if (bestFeatureValue == null)
            {
                bestFeatureData = connectdb.GetUniqueValueAndNum(tbname, feature[0])[0].ToList();
            }
            else
            {
                bestFeatureData = bestFeatureValue.ToList();
            }
            bestFeatureData.Add(bestFeatureIndex);
            return bestFeatureData;
        }

        /// <summary>
        /// 计算熵
        /// </summary>
        /// <param name="classCount">特征属性(类)及对应的个数</param>
        /// <param name="num">总数</param>
        /// <returns>熵</returns>
        private decimal CalculateEntropy(int[][] classCount, decimal num)
        {
            decimal entropy = 0, prob;
            foreach (int count in classCount[1])
            {
                prob = count / num;
                entropy -= prob * Convert.ToDecimal(Math.Log(Convert.ToDouble(prob), 2));
            }
            return entropy;
        }
    }
}
