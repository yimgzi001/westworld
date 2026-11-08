namespace RecoveryVision;

/// <summary>Conservative supercover traversal to a target cell CENTER.
/// isClear must return true ONLY for known-loaded Air. This code does not fetch terrain.</summary>
public static class VoxelVisibility
{
    public static bool HasClearLine(VisionCore.Vec eye,int targetX,int targetY,int targetZ,
        Func<int,int,int,bool> isClear)
    {
        if (isClear is null || !eye.Finite) return false;
        // Leave a guard cell against checked integer overflow during supercover neighbors.
        if (eye.X<=int.MinValue+1d || eye.X>=int.MaxValue-1d ||
            eye.Y<=int.MinValue+1d || eye.Y>=int.MaxValue-1d ||
            eye.Z<=int.MinValue+1d || eye.Z>=int.MaxValue-1d) return false;
        double[] start={eye.X,eye.Y,eye.Z};
        double[] d={targetX+.5-eye.X,targetY+.5-eye.Y,targetZ+.5-eye.Z};
        double distance2=d.Sum(v=>v*v);
        if (!double.IsFinite(distance2) || distance2>VisionCore.Radius*VisionCore.Radius) return false;
        int[] cell={ (int)Math.Floor(eye.X),(int)Math.Floor(eye.Y),(int)Math.Floor(eye.Z) };
        int[] step=d.Select(Math.Sign).ToArray();
        double[] delta=d.Select(v=>v==0?double.PositiveInfinity:1/Math.Abs(v)).ToArray();
        double[] next=new double[3];
        for(int axis=0;axis<3;axis++)
            next[axis]=step[axis]==0 ? double.PositiveInfinity :
                ((step[axis]>0?cell[axis]+1d:cell[axis])-start[axis])/d[axis];
        bool Clear(int x,int y,int z)
        {
            if(x==targetX && y==targetY && z==targetZ) return true;
            try {return isClear(x,y,z);} catch {return false;}
        }
        if(!Clear(cell[0],cell[1],cell[2])) return false;
        // Radius 8 implies fewer than 45 axis crossings. Hard cap fails closed.
        for(int count=0;count<64;count++)
        {
            if(cell[0]==targetX && cell[1]==targetY && cell[2]==targetZ) return true;
            double t=next.Min();
            if(!double.IsFinite(t) || t>1 || t<0) return false;
            int mask=0;
            // Near-ties add conservative obstruction cells, never remove them.
            for(int axis=0;axis<3;axis++) if(Math.Abs(next[axis]-t)<=1e-12) mask|=1<<axis;
            for(int subset=1;subset<8;subset++)
            {
                if((subset&mask)!=subset)continue;
                int x=cell[0]+((subset&1)!=0?step[0]:0);
                int y=cell[1]+((subset&2)!=0?step[1]:0);
                int z=cell[2]+((subset&4)!=0?step[2]:0);
                if(!Clear(x,y,z))return false;
            }
            for(int axis=0;axis<3;axis++)if((mask&(1<<axis))!=0){cell[axis]+=step[axis];next[axis]+=delta[axis];}
        }
        return false;
    }
}
