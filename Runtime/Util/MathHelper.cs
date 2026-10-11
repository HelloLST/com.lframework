using System;
using UnityEngine;

namespace LFramework.Util
{
    public static class MathHelper
    {
        public static float ONE_DIV_PI = 1.0f / Mathf.PI;
        public static Vector3 AxisX = new Vector3(1, 0, 0);
        public static Vector3 AxisY = new Vector3(0, 1, 0);
        public static Vector3 AxisZ = new Vector3(0, 0, 1);
        public static float EPSILON = 0.001f;

        /// <summary>
        /// 四舍五入
        /// </summary>
        /// digits:保留几位小数
        public static float Round(float value, int digits = 1)
        {
            float multiple = Mathf.Pow(10, digits);
            float tempValue = value * multiple + 0.5f;
            tempValue = Mathf.FloorToInt(tempValue);
            return tempValue / multiple;
        }

        /// <summary>
        /// float 近似相等
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static bool IsEqualFloat(float a, float b)
        {
            return (Math.Abs(a - b) < 0.001f);
        }

        /// <summary>
        /// 浮点数保留两位小数点转为字符串显示
        /// </summary>
        /// <param name="f"></param>
        /// <returns></returns>
        public static string FloatToString(float f)
        {
            return f.ToString("F2");
        }

        //转百分比输出字符串显示
        public static string FloatToPerString(float f)
        {
            // 0.1234567
            return f.ToString("P"); //debug--12.35 %; 自动四舍五入，百分数，千位分隔，自动两个小数
                                    //("P1")--保留一位小数
        }

        /// <summary>
        /// 数字格式化显示，不足位数补0
        /// </summary>
        /// <param name="number"></param>
        /// <param name="maxLength">位数</param>
        /// <returns></returns>
        ///   FormatFillZero(5,2)   debug---05
        public static string FormatFillZero(int number, int maxLength)
        {
            if (number.ToString().Length < maxLength)
            {
                var count = maxLength - number.ToString().Length;
                string zeroStr = null;
                for (int idx = 0; idx < count; idx++)
                {
                    zeroStr = string.Concat("0", zeroStr);
                }
                return string.Concat(zeroStr, number);
            }
            return number.ToString();
        }

        /// <summary>
        /// 获取一个范围
        /// </summary>
        /// <param name="startDirection"></param>
        /// <param name="nNum"></param>
        /// <param name="pAnchorPos"></param>
        /// <param name="fAngle"></param>
        /// <param name="nRadius"></param>
        /// <returns></returns>
        public static Vector3[] GetSmartNpcPoints(Vector3 startDirection, int nNum, Vector3 pAnchorPos, float fAngle, float nRadius)
        {
            bool bPlural = nNum % 2 == 0 ? true : false; // 是否复数模式
            Vector3 vDir = startDirection;
            int nMidNum = bPlural ? nNum / 2 : nNum / 2 + 1; // 中间数, 循环过了中间数后，另一个方向起排布
            Vector3 vRPos = vDir * nRadius; //// 计算直线在圆形上的顶点 半径是表现距离
            Vector3[] targetPos = new Vector3[nNum];
            for (int i = 1; i <= nNum; i++)
            {
                float nAddAngle = 0;

                if (bPlural) // 复数模式
                {
                    if (i > nMidNum)
                        nAddAngle = fAngle * (i % nMidNum + 1) - fAngle / 2;
                    else
                        nAddAngle = -fAngle * (i % nMidNum + 1) + fAngle / 2; // 除以2，是为了顶端NPC均匀排布
                }
                else // 单数模式
                {
                    // 判断是否过了中间数
                    if (i > nMidNum)
                    {
                        nAddAngle = fAngle * (i % nMidNum); // 添加NPC的角度
                    }
                    else if (i < nMidNum) // 非复数模式， 中间数NPC 放在正方向
                    {
                        nAddAngle = -fAngle * (i % nMidNum); // 反方向角度
                    }
                    else
                        nAddAngle = 0; // 正方向
                }

                Vector3 vTargetPos = pAnchorPos + Quaternion.AngleAxis(nAddAngle, Vector3.forward) * vRPos;
                targetPos[i - 1] = vTargetPos;
            }
            return targetPos;
        }

        /// <summary>
        /// 解析(1,1,1 或11.1,10,2)为vector3
        /// </summary>
        /// <param name="strVector3"></param>
        /// <param name="splitStr"></param>
        /// <returns></returns>
        public static Vector3 StringToVector3(string strVector3, params char[] splitStr)
        {
            Vector3 ret = new Vector3(0, 0, 0);
            if (!string.IsNullOrEmpty(strVector3))
            {
                var strArr = strVector3.Split(splitStr);
                if (strArr.Length == 3)
                {
                    var x = strArr[0].Trim().Tosingle();
                    var y = strArr[1].Trim().Tosingle();
                    var z = strArr[2].Trim().Tosingle();
                    ret = new Vector3(x, y, z);
                }
                else
                {
                    LogManager.Warning("str length not 3");
                }
            }
            else
            {
                LogManager.Warning("str null");
            }
            return ret;
        }

        /// <summary>
        /// 获取两个点间的夹角
        /// </summary>
        /// <param name="form"></param>
        /// <param name="to"></param>
        /// <returns></returns>
        public static float GetAngle(Vector3 form, Vector3 to)
        {
            Vector3 nVector = Vector3.zero;
            nVector.x = to.x;
            nVector.y = form.y;
            float a = to.y - nVector.y;
            float b = nVector.x - form.x;
            float tan = a / b;
            return Mathf.Atan(tan) * 180.0f * ONE_DIV_PI;
        }

        /// <summary>
        /// 判断向量的大致方向
        /// </summary>
        /// <param name="dir"></param>
        /// <returns></returns>
        public static Vector3 ApproximateDir(Vector3 dir)
        {
            float dotX = Vector3.Dot(dir, AxisX);
            float dotZ = Vector3.Dot(dir, AxisZ);
            if (Mathf.Abs(dotX) > Mathf.Abs(dotZ))
            {
                return dotX > 0 ? AxisX : -AxisX;
            }
            else
            {
                return dotZ > 0 ? AxisZ : -AxisZ;
            }
        }

        /// <summary>
        /// 向量标准化并且返回长度
        /// </summary>
        /// <param name="vec"></param>
        /// <returns></returns>
        public static float Normalize(ref Vector3 vec)
        {
            float length = Mathf.Sqrt((vec.x * vec.x) + (vec.y * vec.y) + (vec.z * vec.z));
            if (length > 0)
            {
                float oneDivLength = 1.0f / length;
                vec.x = vec.x * oneDivLength;
                vec.y = vec.y * oneDivLength;
                vec.z = vec.z * oneDivLength;
            }
            return length;
        }
    }
}