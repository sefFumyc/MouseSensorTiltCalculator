using System;
using System.Collections.Generic;
using System.Linq;

namespace MouseTiltCorrector
{
    // 基础数据点
    public struct MousePoint
    {
        public double dx;
        public double dy;
    }

    // 最小二乘法计算器
    public static class RegressionCalculator
    {
        public static double CalculateAngle(List<MousePoint> points)
        {
            if (points == null || points.Count < 2) return 0;

            double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
            int n = points.Count;

            foreach (var p in points)
            {
                sumX += p.dx;
                sumY += p.dy;
                sumXY += (p.dx * p.dy);
                sumX2 += (p.dx * p.dx);
            }

            double denominator = (n * sumX2) - (sumX * sumX);
            if (Math.Abs(denominator) < 0.0001) return 0;

            double slope = ((n * sumXY) - (sumX * sumY)) / denominator;
            
            // 计算角度并转为度数
            // 注意：屏幕坐标系Y向下，但Raw Input通常是一致的物理移动，
            // 顺时针倾斜通常产生同号斜率。
            double angleDeg = Math.Atan(slope) * (180.0 / Math.PI);
            return angleDeg;
        }
    }

    // 笔触管理器（负责切分和去噪）
    public class StrokeManager
    {
        private List<MousePoint> _buffer = new List<MousePoint>();
        private bool? _movingRight = null;
        private const int MinStrokeLength = 50; // 最小有效长度
        
        // 当一条线收集完成，并通过去噪处理后，触发此事件
        public event Action<double> OnStrokeAnalyzed; 

        public void Process(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return;

            bool inputRight = dx > 0;

            // 第一次移动初始化
            if (_movingRight == null) _movingRight = inputRight;

            // 检测反向：如果方向变了，并且 Buffer 里有货，说明上一笔画完了
            if (inputRight != _movingRight.Value && _buffer.Count > 0)
            {
                FinalizeStroke();
                _movingRight = inputRight; // 翻转方向
            }

            // 记录数据
            _buffer.Add(new MousePoint { dx = dx, dy = dy });
        }

        public void ForceFinalize()
        {
            if (_buffer.Count > 0) FinalizeStroke();
        }

        private void FinalizeStroke()
        {
            // 1. 长度过滤：太短的不要（手抖）
            double totalLen = _buffer.Sum(p => Math.Abs(p.dx));
            if (totalLen < MinStrokeLength)
            {
                _buffer.Clear();
                return;
            }

            // 2. 掐头去尾 (Trimming 10%)
            int count = _buffer.Count;
            int skip = (int)(count * 0.10);
            int take = count - (skip * 2);

            if (take > 5)
            {
                var validPoints = _buffer.Skip(skip).Take(take).ToList();
                
                // 3. 立即计算这一笔的角度
                double angle = RegressionCalculator.CalculateAngle(validPoints);
                OnStrokeAnalyzed?.Invoke(angle);
            }

            _buffer.Clear();
        }
    }
}