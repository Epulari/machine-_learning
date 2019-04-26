using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DecisionTree
{
    class Program
    {
        static void Main(string[] args)
        {
            // abcd为一组特征，决策树直接根据这组特征建立决策树
            List<string> feature = new List<string>() { "a", "b", "c", "d" };
            ClassicDecisionTree cd = new ClassicDecisionTree("test", "classification");
            Tree t1 = cd.CreateTree("rf1", "rf1", feature.ToList(), true);
            
            // abcd分为两组特征，决策树先根据ad建立，然后再根据bc建立
            List<string> treeFeature = new List<string>() { "a", "b", "c", "d" };
            List<string>[] treeFeatureAsType = new List<string>[2];
            treeFeatureAsType[0] = new List<string>() { "a", "d" };
            treeFeatureAsType[1] = new List<string>() { "c", "b" };
            MultipleClassicDecisionTree mcd = new MultipleClassicDecisionTree("test", "classification", treeFeatureAsType);
            Tree t2 = mcd.CreateTree("rf1", "rf1", treeFeature, treeFeatureAsType[0], 1);
        }
    }
}
