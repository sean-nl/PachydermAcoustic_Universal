﻿using System;
using System.Collections.Generic;
using Pachyderm_Acoustic.Environment;
using Hare.Geometry;

namespace Pachyderm_Acoustic
{
    namespace Numeric
    {
        namespace TimeDomain
        {
            public partial class Acoustic_Compact_FDTD : Simulation_Type
            {
                Polygon_Scene Rm;
                public double tmax;       //seconds
                private double rho0 = 1.21;    //density of air kg/m^3
                public double dt;
                public double dx, dy, dz;
                public readonly int xDim, yDim, zDim;
                protected Node[][][] PFrame;            //pressure scalar field initialisation
                Vector[] Dir = new Vector[13];
                Point[] Orig = new Point[13];
                double fmax;
                AABB Bounds;
                PML Layers;
                public Signal_Driver_Compact SD;
                public Microphone_Compact Mic;
                public int n;
                double time_ms;
                
                public Acoustic_Compact_FDTD(Polygon_Scene Rm_in, ref Signal_Driver_Compact S_in, ref Microphone_Compact M_in, double fmax_in, double tmax_in)
                {
                    Rm = Rm_in;
                    SD = S_in;
                    Mic = M_in;
                    fmax = fmax_in;
                    tmax = tmax_in;
                    Rm.partition(20);
                    
                    Build_FVM13(ref xDim, ref yDim, ref zDim, true);
                }

                public void Build_FVM13(ref int xDim, ref int yDim, ref int zDim, bool PML)
                {
                    double rt2 = Math.Sqrt(2);
                    double rt3 = Math.Sqrt(3);

                    dx = Rm.Sound_speed(0) / fmax * .1;

                    Bounds = new AABB(Rm.Min() - new Point(.05 * dx, .05 * dx, .05 * dx), Rm.Max() + new Point(.05 * dx, .05 * dx, .05 * dx));

                    int no_of_Layers = 0;
                    double max_Layer = 0;

                    if (PML)
                    {
                        no_of_Layers = 10;
                        max_Layer = 0.5;
                    }

                    double x_length = Bounds.X_Length() + (no_of_Layers * 2 + 1) * dx;
                    double y_length = Bounds.Y_Length() + (no_of_Layers * 2 + 1) * dx;
                    double z_length = Bounds.Z_Length() + (no_of_Layers * 2 + 1) * dx;

                    //estimated distance between nodes
                    xDim = (int)Math.Ceiling(x_length / dx);                                //set number of nodes in x direction
                    dx = x_length / xDim;                                                   //refined distance between nodes
                    yDim = (int)Math.Ceiling(y_length / dx);                                //set number of nodes in y direction
                    dy = y_length / yDim;
                    zDim = (int)Math.Ceiling(z_length / dx);                                //set number of nodes in z direction
                    dz = z_length / zDim;

                    //dt = dx / (Math.Sqrt(1.0 / 3.0) * (Rm.Sound_speed(0)));                           //set time step small enough to satisfy courrant condition
                    dt = dx / (Rm.Sound_speed(0));                           //set time step small enough to satisfy courrant condition
                    //dt = dx / (Math.Sqrt(.75) * (Rm.Sound_speed));                           //set time step small enough to satisfy courrant condition
                    dxrt2 = dx * rt2;
                    dxrt3 = dx * rt3;

                    Dir = new Vector[13]{
                        //new Vector(-1,double.Epsilon,double.Epsilon),
                        //new Vector(double.Epsilon,-1,double.Epsilon),
                        //new Vector(double.Epsilon,double.Epsilon,-1),

                        //new Vector(-1/rt2,-1/rt2,double.Epsilon),
                        //new Vector(1/rt2, -1/rt2,double.Epsilon),
                        //new Vector(-1/rt2, double.Epsilon, 1/rt2),
                        //new Vector(double.Epsilon, -1/rt2, 1/rt2),
                        //new Vector(1/rt2, double.Epsilon, 1/rt2),
                        //new Vector(double.Epsilon, 1/rt2, 1/rt2),

                        //new Vector(-1/rt2,-1/rt2,1/rt2),
                        //new Vector(1/rt2,-1/rt2,1/rt2),
                        //new Vector(1/rt2,1/rt2,1/rt2),
                        //new Vector(-1/rt2,1/rt2,1/rt2)
                        new Vector(-1, 0, 0),
                        new Vector(0, -1, 0),
                        new Vector(0, 0, -1),

                        new Vector(-1/rt2,-1/rt2,double.Epsilon),
                        new Vector(1/rt2, -1/rt2,double.Epsilon),
                        new Vector(-1/rt2, double.Epsilon, 1/rt2),
                        new Vector(double.Epsilon, -1/rt2, 1/rt2),
                        new Vector(1/rt2, double.Epsilon, 1/rt2),
                        new Vector(double.Epsilon, 1/rt2, 1/rt2),

                        new Vector(-dx, -dy/rt2, dz/rt2),
                        new Vector(dx, -dy/rt2, dz/rt2),
                        new Vector(dx, dy/rt2, dz/rt2),
                        new Vector(-dx, dy/rt2, dz/rt2)

                    };

                    foreach (Vector V in Dir) V.Normalize();

                    xDim = (int)Math.Ceiling((double)xDim * rt2 / 2);

                    PFrame = new Node[xDim][][];// yDim, zDim];                               //pressure scalar field initialisation

                    List<Bound_Node_RDD> Bound = new List<Bound_Node_RDD>();

                    //dx = dx/rt2;
                    Point MinPt = Bounds.Min_PT - new Point(dx * no_of_Layers, dy * no_of_Layers, dz * no_of_Layers);

                    //System.Threading.Tasks.Parallel.For(0, xDim, (x) =>
                    for (int x = 0; x < PFrame.Length; x++)
                    {
                        int mod = x % 2;
                        PFrame[x] = new Node[(int)(Math.Floor((double)yDim / 2) + yDim % 2 * mod)][];
                        Random Rnd = new Random(x);
                        for (int y = 0; y < PFrame[x].Length; y++)
                        {
                            PFrame[x][y] = new Node[(int)(Math.Floor((double)zDim / 2) + yDim % 2 * mod)];
                            for (int z = 0; z < PFrame[x][y].Length; z++)
                            {
                                List<double> abs;
                                List<Bound_Node.Boundary> BDir;
                                Point Loc = new Point(MinPt.x + 2 * (((double)x - 0.5) * dx / rt2), MinPt.y + 2 * (((double)y + (0.5 - 0.5 * mod)) * dy), MinPt.z + 2 * (((double)z + (0.5 - 0.5 * mod)) * dz));
                                if (!Intersect_13Pt(Loc, SD.frequency, out BDir, out abs, ref Rnd))
                                {
                                    PFrame[x][y][z] = new RDD_Node(Loc);//, rho0, dt, dx, Rm.Sound_speed, new int[] { x, y, z });
                                }
                                else
                                {
                                    PFrame[x][y][z] =
                                        new Bound_Node_RDD(Loc, rho0, dt, dx, Rm.Sound_speed(0), new int[] { x, y, z }, abs, BDir);
                                    Bound.Add(PFrame[x][y][z] as Bound_Node_RDD);
                                }
                            }
                        }
                    }//);

                    Node.Attenuation = Math.Sqrt(Math.Pow(10, -.1 * Rm.AttenuationPureTone(PFrame[0][0][0].Pt, SD.frequency) * dt));

                    bool failed = false;
                    for (int x = 0; x < PFrame.Length; x++)
                    //System.Threading.Tasks.Parallel.For(0, xDim, (x) =>
                    {
                        for (int y = 0; y < PFrame[x].Length; y++)
                        {
                            for (int z = 0; z < PFrame[x][y].Length; z++)
                            {
                                PFrame[x][y][z].Link_Nodes(ref PFrame, x, y, z);
                            }
                        }
                    }//);

                    yDim = PFrame[0].Length;
                    zDim = PFrame[0][0].Length;

                    foreach (Bound_Node_RDD b in Bound) b.Complete_Boundary();
                    if (failed) return;

                    //Set up PML...
                    Layers = new Acoustic_Compact_FDTD.PML(no_of_Layers, max_Layer, PFrame);

                    //Connect Sources and Receivers...
                    SD.Connect_Grid(PFrame, Bounds, dx, dy, dz, tmax, dt, no_of_Layers);
                    Mic.Connect_Grid(PFrame, Bounds, dx, tmax, dt, no_of_Layers);

                }

                public double t;

                public void RuntoCompletion()
                {
                    for(time_ms = 0; time_ms < tmax/1000; time_ms+= dt)
                    {
                        t = Increment();
                    }
                }

                public double SampleFrequency
                {
                    get { return 1f / dt; }
                }

                public void reset(double frequency, Signal_Driver_Compact.Signal_Type s)
                {
                    SD.reset(frequency, s);
                    Mic.reset();
                    time_ms = 0;
                    n = 0;
                    for (int X = 0; X < PFrame.Length; X++)
                        for (int Y = 0; Y < PFrame[X].Length; Y++)
                            for (int Z = 0; Z < PFrame[X][Y].Length; Z++)
                                PFrame[X][Y][Z].reset();
                }

                public double Increment()
                {
                    List<double> Pressure = new List<double>();

                    System.Threading.Tasks.Parallel.For(0, PFrame.Length, (x) =>
                    {
                        for (int y = 0; y < PFrame[x].Length; y++)
                        {
                            for (int z = 0; z < PFrame[x][y].Length; z++)
                            {
                                PFrame[x][y][z].UpdateP();
                            }
                        }
                    });

                    System.Threading.Tasks.Parallel.For(0, PFrame.Length, (x) =>
                    {
                        for (int y = 0; y < PFrame[x].Length; y++)
                        {
                            for (int z = 0; z < PFrame[x][y].Length; z++)
                            {
                                PFrame[x][y][z].UpdateT();
                            }
                        }
                    });

                    Layers.Attenuate();

                    SD.Drive(n);
                    Mic.Record(n);
                    n += 1;
                    time_ms = n * dt;
                    return time_ms;
                }

                double dxrt2;
                double dxrt3;

                public bool Intersect_13Pt(Point Center, double frequency, out List<Bound_Node.Boundary> Bout, out List<double> alpha, ref Random Rnd)
                {
                    Bout = new List<Bound_Node.Boundary>();
                    alpha = new List<double>();

                    Center += new Point((Rnd.NextDouble() - .5) * 1E-6, (Rnd.NextDouble() - .5) * 1E-6, (Rnd.NextDouble() - .5) * 1E-6);

                    X_Event XPt = new X_Event();

                    double dx2 = 2 * dx + double.Epsilon;

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, -1 * Dir[1], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[1] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.AYPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[1], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[1] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.AYNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, -1 * Dir[2], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[2] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.AZPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[2], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[2] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.AZNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[9], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[9] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXNegYNegZPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[9] * -1, 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[9] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXPosYPosZNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[10], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[10] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXPosYNegZPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[10] * -1, 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[10] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXNegYPosZNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[11], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[11] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXPosYPosZPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[11] * -1, 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[11] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXNegYNegZNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }

                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[12], 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center + Dir[12] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXNegYPosZPos);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    XPt = new X_Event();
                    if (Rm.shoot(new Ray(Center, Dir[12] * -1, 0, Rnd.Next()), 0, out XPt) && XPt.t < dx2)
                    {
                        //Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(new Rhino.Geometry.Line(Utilities.PachTools.HPttoRPt(Center), Utilities.PachTools.HPttoRPt(Center - Dir[12] * 2 * dx)));
                        Bout.Add(Bound_Node.Boundary.DXPosYNegZNeg);
                        alpha.Add(Rm.AbsorptionValue[XPt.Poly_id].Reflection_Narrow(frequency).Magnitude);
                    }
                    return Bout.Count > 0;
                }

                public override string ProgressMsg()
                {
                    throw new NotImplementedException();
                }

                public override System.Threading.ThreadState ThreadState()
                {
                    throw new NotImplementedException();
                }

                public override void Abort_Calculation()
                {
                    throw new NotImplementedException();
                }

                public override void Combine_ThreadLocal_Results()
                {
                    throw new NotImplementedException();
                }

                public override void Begin()
                {
                    throw new NotImplementedException();
                }

                public override string Sim_Type()
                {
                    return "Finite Difference Time Domain";
                }

                public double P(int x, int y, int z)
                {
                    return this.PFrame[x][y][z].P;
                }

                public void Pressure_Points(ref List<List<Hare.Geometry.Point>> Pts, ref List<List<double>> Pressure, int[] X, int[] Y, int[] Z, double Low_P, bool Volume, bool Vectored, bool Colored, bool Magnitude)
                {
                    Pts = new List<List<Hare.Geometry.Point>>();
                    Pressure = new List<List<double>>();
                    if (Volume)
                    {
                        for (int x = 0; x < xDim; x++)
                        {
                            List<double> P = new List<double>();
                            List<Hare.Geometry.Point> PtList = new List<Hare.Geometry.Point>();
                            //System.Threading.Tasks.Parallel.For(0, yDim, (y) =>
                            for (int y = 0; y < yDim; y++)
                            {
                                for (int z = 0; z < zDim; z++)
                                {
                                    if (PFrame[x][y][z].P < Low_P) continue;
                                    if (Colored) P.Add(PFrame[x][y][z].P);
                                    //if (Vectored)
                                    //{
                                    //    PtList.Add((new Rhino.Geometry.Point3d(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z) + PFrame[x][y][z].VelocityDirection() * 3 * dx * (Magnitude ? PFrame[x][y][z].P: PFrame[x][y][z].P) ));
                                    //}
                                    //else
                                    //{
                                    PtList.Add((new Hare.Geometry.Point(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z)));
                                    //}
                                }
                            }//);
                            Pressure.Add(P);
                            Pts.Add(PtList);
                        }
                        return;
                    }
                    if (X != null || X.Length > 0)
                    {
                        foreach (int x in X)
                        {
                            List<double> P = new List<double>();
                            List<Hare.Geometry.Point> PtList = new List<Hare.Geometry.Point>();
                            //System.Threading.Tasks.Parallel.For(0, yDim, (y) =>
                            for (int y = 0; y < PFrame[x].Length; y++)
                            {
                                for (int z = 0; z < PFrame[x][y % 2].Length; z++)
                                {
                                    lock (Pts)
                                    {
                                        if (Colored) P.Add(PFrame[x][y][z].P);
                                        //if (Vectored)
                                        //{
                                        //    PtList.Add((new Rhino.Geometry.Point3d(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z) + PFrame[x][y][z].VelocityDirection() * PFrame[x][y][z].P * 3 * dx));
                                        //}
                                        //else
                                        //{
                                        PtList.Add((new Hare.Geometry.Point(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z)));
                                        //}
                                    }
                                }
                            }//);
                            Pressure.Add(P);
                            Pts.Add(PtList);
                        }
                    }
                    if (Y != null || Y.Length > 0)
                    {
                        foreach (int y in Y)
                        {
                            List<double> P = new List<double>();
                            List<Hare.Geometry.Point> PtList = new List<Hare.Geometry.Point>();
                            //System.Threading.Tasks.Parallel.For(0, xDim, (x) =>
                            for (int x = 0; x < PFrame.Length; x++)
                            {
                                for (int z = 0; z < PFrame[x][y % 2].Length; z++)
                                {
                                    lock (Pts)
                                    {
                                        if (Colored) P.Add(PFrame[x][y][z].P);
                                        //if (Vectored)
                                        //{
                                        //    PtList.Add((new Rhino.Geometry.Point3d(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z) + PFrame[x][y][z].VelocityDirection() * PFrame[x][y][z].P * 3 * dx));
                                        //}
                                        //else
                                        //{
                                        PtList.Add((new Hare.Geometry.Point(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z)));
                                        //}
                                    }
                                }
                            }//);
                            Pressure.Add(P);
                            Pts.Add(PtList);
                        }
                    }
                    if (Z != null || Z.Length > 0)
                    {
                        foreach (int z in Z)
                        {
                            List<double> P = new List<double>();
                            List<Hare.Geometry.Point> PtList = new List<Hare.Geometry.Point>();
                            //System.Threading.Tasks.Parallel.For(0, xDim, (x) =>
                            for (int x = 0; x < PFrame.Length; x++)
                            {
                                for (int y = 0; y < PFrame[x].Length; y++)
                                {
                                    lock (Pts)
                                    {
                                        if (Colored) P.Add(PFrame[x][y][z].P);
                                        //if (Vectored)
                                        //{
                                        //    PtList.Add((new Rhino.Geometry.Point3d(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z) + PFrame[x][y][z].VelocityDirection() * PFrame[x][y][z].P * 3 * dx));
                                        //}
                                        //else
                                        //{
                                        PtList.Add((new Hare.Geometry.Point(PFrame[x][y][z].Pt.x, PFrame[x][y][z].Pt.y, PFrame[x][y][z].Pt.z)));
                                        //}
                                    }
                                }
                            }//);
                            Pressure.Add(P);
                            Pts.Add(PtList);
                        }
                    }
                }

                public abstract class Node
                {
                    public Point Pt;
                    public double Pnf, Pn, Pn_1;

                    public abstract void Link_Nodes(ref Node[][][] Frame, int x, int y, int z);
                    public abstract void UpdateP();
                    public static double Attenuation = 1;

                    public Node(Point loc)
                    {
                        Pt = loc;
                    }

                    public virtual void UpdateT()
                    {
                        Pn_1 = Pn;
                        Pn = Pnf * Attenuation;
                    }

                    public void reset()
                    {
                        Pnf = 0; Pn = 0; Pn_1 = 0;
                    }

                    /// <summary>
                    /// Call after UpdateT, to levy additional attenuation...
                    /// </summary>
                    /// <param name="coefficient"></param>
                    public void Attenuate(double coefficient)
                    {
                        Pn *= coefficient;
                    }

                    //public Rhino.Geometry.Vector3d VelocityDirection()
                    //{
                    //    Rhino.Geometry.Vector3d V = new Rhino.Geometry.Vector3d(X, Y, Z);
                    //    V.Unitize();
                    //    return V;
                    //}

                    public double P
                    {
                        get
                        {
                            return Pn;
                        }
                    }
                }

                public class P_Node : Node
                {
                    protected double X, Y, Z;
                    protected Node Xpos_Link;
                    protected Node Ypos_Link;
                    protected Node Zpos_Link;
                    protected Node Xneg_Link;
                    protected Node Yneg_Link;
                    protected Node Zneg_Link;

                    protected Node[] Links2;
                    protected Node[] Links3;

                    public P_Node(Point loc)
                        : base(loc)
                    { }

                    public override void Link_Nodes(ref Node[][][] Frame, int x, int y, int z)
                    {
                        int xdim = Frame.Length - 1;
                        int ydim = Frame[0].Length - 1;
                        int zdim = Frame[0][0].Length - 1;

                        if (x < xdim)
                        {
                            Xpos_Link = Frame[x + 1][y][z] as P_Node;
                        }
                        else Xpos_Link = new Null_Node();
                        //FrameX[x + 1, y, z].id.AddRange(id);
                        if (y < ydim)
                        {
                            Ypos_Link = Frame[x][y + 1][z] as P_Node;
                        }
                        else Ypos_Link = new Null_Node();
                        //FrameY[x, y + 1, z].id.AddRange(id);
                        if (z < zdim)
                        {
                            Zpos_Link = Frame[x][y][z + 1] as P_Node;
                        }
                        else Zpos_Link = new Null_Node();
                        //FrameZ[x, y, z + 1].id.AddRange(id);
                        if (x > 0)
                        {
                            Xneg_Link = Frame[x - 1][y][z] as P_Node;
                        }
                        else Xneg_Link = new Null_Node();
                        //FrameX[x, y, z].id.AddRange(id);
                        if (y > 0)
                        {
                            Yneg_Link = Frame[x][y - 1][z] as P_Node;
                        }
                        else Yneg_Link = new Null_Node();
                        //FrameY[x, y, z].id.AddRange(id);
                        if (z > 0)
                        {
                            Zneg_Link = Frame[x][y][z - 1] as P_Node;
                        }
                        else Zneg_Link = new Null_Node();
                        //FrameZ[x, y, z].id.AddRange(id);

                        Links2 = new P_Node[12];
                        Links3 = new P_Node[8];

                        if (x < xdim && y < ydim) Links2[0] = Frame[x + 1][y + 1][z] as P_Node; else Links2[0] = new Null_Node();
                        if (x > 0 && y > 0) Links2[1] = Frame[x - 1][y - 1][z] as P_Node; else Links2[1] = new Null_Node();
                        if (x < xdim && y > 0) Links2[2] = Frame[x + 1][y - 1][z] as P_Node; else Links2[2] = new Null_Node();
                        if (x > 0 && y < ydim) Links2[3] = Frame[x - 1][y + 1][z] as P_Node; else Links2[3] = new Null_Node();
                        if (x < xdim && z < zdim) Links2[4] = Frame[x + 1][y][z + 1] as P_Node; else Links2[4] = new Null_Node();
                        if (x > 0 && z > 0) Links2[5] = Frame[x - 1][y][z - 1] as P_Node; else Links2[5] = new Null_Node();
                        if (x > 0 && z < zdim) Links2[6] = Frame[x - 1][y][z + 1] as P_Node; else Links2[6] = new Null_Node();
                        if (x < xdim && z > 0) Links2[7] = Frame[x + 1][y][z - 1] as P_Node; else Links2[7] = new Null_Node();
                        if (y < ydim && z < zdim) Links2[8] = Frame[x][y + 1][z + 1] as P_Node; else Links2[8] = new Null_Node();
                        if (y > 0 && z > 0) Links2[9] = Frame[x][y - 1][z - 1] as P_Node; else Links2[9] = new Null_Node();
                        if (y > 0 && z < zdim) Links2[10] = Frame[x][y - 1][z + 1] as P_Node; else Links2[10] = new Null_Node();
                        if (y < ydim && z > 0) Links2[11] = Frame[x][y + 1][z - 1] as P_Node; else Links2[11] = new Null_Node();

                        if (x < xdim && y < ydim && z < zdim) Links3[0] = Frame[x + 1][y + 1][z + 1] as P_Node; else Links3[0] = new Null_Node();
                        if (x > 0 && y > 0 && z > 0) Links3[1] = Frame[x - 1][y - 1][z - 1] as P_Node; else Links3[1] = new Null_Node();
                        if (x > 0 && y > 0 && z < zdim) Links3[2] = Frame[x - 1][y - 1][z + 1] as P_Node; else Links3[2] = new Null_Node();
                        if (x < xdim && y < ydim && z > 0) Links3[3] = Frame[x + 1][y + 1][z - 1] as P_Node; else Links3[3] = new Null_Node();
                        if (x > 0 && y < ydim && z < zdim) Links3[4] = Frame[x - 1][y + 1][z + 1] as P_Node; else Links3[4] = new Null_Node();
                        if (x < xdim && y > 0 && z > 0) Links3[5] = Frame[x + 1][y - 1][z - 1] as P_Node; else Links3[5] = new Null_Node();
                        if (x > 0 && y < ydim && z > 0) Links3[6] = Frame[x - 1][y + 1][z - 1] as P_Node; else Links3[6] = new Null_Node();
                        if (x < xdim && y > 0 && z < zdim) Links3[7] = Frame[x + 1][y - 1][z + 1] as P_Node; else Links3[7] = new Null_Node();
                    }

                    //public virtual void UpdateSLF()
                    //{
                    //    X = Xpos_Link.P + Xneg_Link.P;
                    //    Y = Ypos_Link.P + Yneg_Link.P;
                    //    Z = Zpos_Link.P + Zneg_Link.P;
                    //    Pnf = Courrant2 * (X + Y + Z) - Pn_1;
                    //}

                    //public virtual void UpdateOCTA()
                    //{
                    //    double p3 = 0;
                    //    foreach (P_Node node in Links3) p3 += node.P;
                    //    Pnf += p3 / 4 - Pn_1; ;
                    //}

                    //public virtual void UpdateCCP()
                    //{
                    //    double p2 = 0;
                    //    foreach (P_Node node in Links2) p2 += node.P;
                    //    Pnf += p2 / 4 - Pn - Pn_1;
                    //}

                    public override void UpdateP()
                    {
                        X = Xpos_Link.P + Xneg_Link.P;
                        Y = Ypos_Link.P + Yneg_Link.P;
                        Z = Zpos_Link.P + Zneg_Link.P;
                        ////IWB
                        //Pnf = 0.25 * (X + Y + Z) - 1.5 * Pn - Pn_1;
                        //double p2 = 0;
                        //foreach (P_Node node in Links2) p2 += node.P;
                        //Pnf += p2 * 0.125;
                        //double p3 = 0;
                        //foreach (P_Node node in Links3) p3 += node.P;
                        //Pnf += p3 * 0.0625;
                        ////IISO2
                        Pnf = (15.0 / 48.0) * (X + Y + Z) - (9.0 / 8.0) * Pn - Pn_1;
                        double p2 = 0;
                        foreach (P_Node node in Links2) p2 += node.P;
                        Pnf += p2 * (3.0 / 32.0);
                        double p3 = 0;
                        foreach (P_Node node in Links3) p3 += node.P;
                        Pnf += p3 * (1.0 / 64.0);
                        Pnf -= (9d / 8d) * Pn - Pn_1;
                        ////IISO
                        //Pnf = (.25) * (X + Y + Z) - Pn - Pn_1;
                        //double p2 = 0;
                        //foreach (P_Node node in Links2) p2 += node.P;
                        //Pnf += p2 * (.125);
                        ////CCP
                        //Pnf = -Pn - Pn_1;//-Pn - Pn_1;
                        //double p2 = 0;
                        //foreach (P_Node node in Links2) p2 += node.P;
                        //Pnf += p2 * (.25);
                        ////OCTA
                        //Pnf = -Pn_1;
                        //double p3 = 0;
                        //foreach (P_Node node in Links3) p3 += node.P;
                        //Pnf += p3 * (.25);
                        ////SLF
                        //Pnf = (1/3) * (X + Y + Z) - (9.0/8.0) * Pn - Pn_1;
                    }
                }

                public class RDD_Node : Node
                {
                    public Node[] Links2;
                    public RDD_Node(Point loc)//, double rho0, double dt, double dx, double c, int[] id_in)
                    : base(loc)
                    { }

                    public override void Link_Nodes(ref Node[][][] Frame, int x, int y, int z)
                    {
                        int mod = x % 2;

                        int xdim = Frame.Length - 1;
                        int ydim = Frame[mod].Length - 1;
                        int zdim = Frame[mod][0].Length - 1;

                        Links2 = new Node[12];

                        /*
                        [0] = x+y+z+
                        [1] = x+y-z-
                        [2] = x+y-z+
                        [3] = x+y+z-
                        [4] = x-y+z+
                        [5] = x-y-z-
                        [6] = x-y-z+
                        [7] = x-y+z-
                        [8] = y+
                        [9] = y-
                        [10] = z+
                        [11] = z-
                        */

                        if (mod == 0)
                        {
                            if (x < xdim)
                            {
                                if (y < ydim && z < zdim) Links2[0] = Frame[x + 1][y + 1][z + 1]; else Links2[0] = new Null_Node();
                                if (y > 0 && z > 0) Links2[1] = Frame[x + 1][y][z]; else Links2[1] = new Null_Node();
                                if (y > 0 && z < zdim) Links2[2] = Frame[x + 1][y][z + 1]; else Links2[2] = new Null_Node();
                                if (y < ydim && z > 0) Links2[3] = Frame[x + 1][y + 1][z]; else Links2[3] = new Null_Node();
                            }
                            else for (int i = 0; i < 4; i++) Links2[i] = new Null_Node();
                            if (x > 0)
                            {
                                if (y < ydim && z < zdim) Links2[4] = Frame[x - 1][y + 1][z + 1]; else Links2[4] = new Null_Node();
                                if (y > 0 && z > 0) Links2[5] = Frame[x - 1][y][z]; else Links2[5] = new Null_Node();
                                if (y > 0 && z < zdim) Links2[6] = Frame[x - 1][y][z + 1]; else Links2[6] = new Null_Node();
                                if (y < ydim && z > 0) Links2[7] = Frame[x - 1][y + 1][z]; else Links2[7] = new Null_Node();
                            }
                            else for (int i = 4; i < 8; i++) Links2[i] = new Null_Node();
                        }
                        else
                        {
                            if (x < xdim)
                            {
                                if (y < ydim && z < zdim) Links2[0] = Frame[x + 1][y][z]; else Links2[0] = new Null_Node();
                                if (y > 0 && z > 0) Links2[1] = Frame[x + 1][y - 1][z - 1]; else Links2[1] = new Null_Node();
                                if (y > 0 && z < zdim) Links2[2] = Frame[x + 1][y - 1][z]; else Links2[2] = new Null_Node();
                                if (y < ydim && z > 0) Links2[3] = Frame[x + 1][y][z - 1]; else Links2[3] = new Null_Node();
                            }
                            else for (int i = 0; i < 4; i++) Links2[i] = new Null_Node();
                            if (x > 0)
                            {
                                if (y < ydim && z < zdim) Links2[4] = Frame[x - 1][y][z]; else Links2[4] = new Null_Node();
                                if (y > 0 && z > 0) Links2[5] = Frame[x - 1][y - 1][z - 1]; else Links2[5] = new Null_Node();
                                if (y > 0 && z < zdim) Links2[6] = Frame[x - 1][y - 1][z]; else Links2[6] = new Null_Node();
                                if (y < ydim && z > 0) Links2[7] = Frame[x - 1][y][z - 1]; else Links2[7] = new Null_Node();
                            }
                            else for (int i = 4; i < 8; i++) Links2[i] = new Null_Node();
                        }

                        if (y < ydim) Links2[8] = Frame[x][y + 1][z]; else Links2[8] = new Null_Node();
                        if (y > 0) Links2[9] = Frame[x][y - 1][z]; else Links2[9] = new Null_Node();
                        if (z < zdim) Links2[10] = Frame[x][y][z + 1]; else Links2[10] = new Null_Node();
                        if (z > 0) Links2[11] = Frame[x][y][z - 1]; else Links2[11] = new Null_Node();

                        //foreach (Node n in Links2) Rhino.RhinoDoc.ActiveDoc.Objects.AddLine(Utilities.PachTools.HPttoRPt(Pt), Utilities.PachTools.HPttoRPt(n.Pt));
                    }

                    public override void UpdateP()
                    {
                        double p2 = 0;
                        foreach (Node node in Links2) p2 += node.P;
                        Pnf = p2 * 0.25 - Pn - Pn_1;
                    }
                }

                public class Null_Node : Node
                {
                    public Null_Node()//Point loc, double rho0, double dt, double dx, double c, int[] id_in)
                        : base(new Point())//, rho0, dt, dx, c, id_in)
                    {
                        Instance = this;
                    }

                    public static Null_Node Instance
                    {
                        get;
                        private set;
                    }

                    public override void UpdateP()
                    {
                        Pnf = 0;
                        Pn = 0;
                        Pn_1 = 0;
                    }

                    public override void UpdateT()
                    {
                    }

                    public override void Link_Nodes(ref Node[][][] Frame, int x, int y, int z)
                    {
                    }
                }

                private class PML
                {
                    List<Node>[] Layers;
                    double[] Coefficients;
                    int layerCt;

                    public PML(int no_of_Layers, double max_Coefficient, Node[][][] PFrame)
                    {
                        Layers = new List<Node>[no_of_Layers / 2];
                        Coefficients = new double[no_of_Layers / 2];
                        layerCt = no_of_Layers / 2;

                        for (int i = 0; i < layerCt; i++)
                        {
                            Coefficients[i] = 1 - max_Coefficient * (layerCt - i) / layerCt;
                            Layers[i] = new List<Node>();
                            for (int y = 0; y < PFrame[i].Length - i; y++) for (int z = 0; z < PFrame[i][y].Length - i; z++) Layers[i].Add(PFrame[i][y][z]);
                            int last = PFrame.Length - 1 - i;
                            for (int y = 0; y < PFrame[last].Length - i; y++) for (int z = 0; z < PFrame[last][y].Length - i; z++) Layers[i].Add(PFrame[last][y][z]);
                            int end = PFrame.Length - i - 1;
                            for (int x = i + 1; x < end; x++)
                            {
                                for (int j = i; j < PFrame[x][i].Length; j++) Layers[i].Add(PFrame[x][i][j]);
                                for (int j = i; j < PFrame[x][PFrame[x].Length - i - 1].Length; j++) Layers[i].Add(PFrame[x][PFrame[x].Length - i - 1][j]);
                                for (int j = i + 1; j < PFrame[x].Length - 1; j++)
                                {
                                    Layers[i].Add(PFrame[x][j][i]);
                                    Layers[i].Add(PFrame[x][j][PFrame[x][j].Length - 1 - i]);
                                }
                            }
                        }
                    }

                    public void Attenuate()
                    {
                        for (int i = 0; i < layerCt; i++)
                        {
                            foreach (Node n in Layers[i])
                            {
                                n.Attenuate(Coefficients[i]);
                            }
                        }
                    }
                }
            }

            public class Signal_Driver_Compact
            {
                Acoustic_Compact_FDTD.Node[] SrcNode;
                double[] signal;
                double f;
                Hare.Geometry.Point[] Loc;
                Signal_Type S;
                double w;
                double tmax;
                double dt;

                public enum Signal_Type
                {
                    Dirac_Pulse,
                    Sine_Tone,
                    Gaussian_Pulse,
                    Sine_Pulse,
                    SteadyState_Noise,
                    SS_Noise_Pulse
                }

                public Signal_Driver_Compact(Signal_Type S_in, double freq, double w_in, Hare.Geometry.Point[] Loc_in)
                {
                    S = S_in;
                    f = freq;
                    Loc = Loc_in;
                    w = w_in;
                }

                public void Connect_Grid(Acoustic_Compact_FDTD.Node[][][] Frame, AABB Bounds, double dx, double dy, double dz, double _tmax, double _dt, int no_of_Layers)
                {
                    tmax = _tmax / 1000;
                    dt = _dt;

                    SrcNode = new Acoustic_Compact_FDTD.Node[Loc.Length];

                    for (int i = 0; i < Loc.Length; i++)
                    {
                        int X = (int)Math.Floor((Loc[i].x - Bounds.Min_PT.x) / (2 * dx / Math.Sqrt(2))) + (int)(no_of_Layers) - 1;
                        int Y = (int)Math.Floor((Loc[i].y - Bounds.Min_PT.y) / (2 * dy)) + no_of_Layers / 2;
                        int Z = (int)Math.Floor((Loc[i].z - Bounds.Min_PT.z) / (2 * dz)) + no_of_Layers / 2;
                        SrcNode[i] = Frame[X][Y][Z];
                    }

                    Generate_Signal();
                }

                public void reset(double frequency, Signal_Type s)
                {
                    f = frequency;
                    S = s;
                    Generate_Signal();
                }

                private void Generate_Signal()
                {
                    signal = new double[(int)Math.Ceiling(tmax / dt)];

                    double f2pi = f * 2 * Math.PI;
                    Random R = new Random((int)System.DateTime.Now.Ticks);

                    switch (S)
                    {
                        case Signal_Type.Dirac_Pulse:
                            signal[1] = 1;
                            break;
                        case Signal_Type.Sine_Tone:
                            double sum_e = 0;
                            for (int n = 0; n < tmax / dt; n++)
                            {
                                double ph = f2pi * n * dt;
                                signal[n] = Math.Sin(ph);
                                sum_e += signal[n] * signal[n];
                            }
                            for (int i = 0; i < signal.Length; i++) signal[i] *= tmax * 20 / Math.Sqrt(sum_e);
                            break;
                        case Signal_Type.Gaussian_Pulse:
                            double sum = 0;
                            for (int n = 0; n < tmax / dt; n++)
                            {
                                signal[n] = Math.Exp(-.5 * Math.Pow((double)n / w, 2));
                                sum += signal[n];
                            }
                            for (int n = 0; n < tmax / dt; n++)
                            {
                                signal[n] /= sum;
                            }
                            break;
                        case Signal_Type.Sine_Pulse:
                            //for (int n = 0; n < tmax / dt; n++) signal[n] = Math.Exp(-.5 * Math.Pow((double)n / 1, 2)) * Math.Sin(f2pi * n * dt);
                            double kk = Math.PI / 60 / 343 / dt;
                            double offset = Math.PI / kk / 343;
                            signal = new double[60];
                            double param = 2 * (0.371 * 60 - 8.1);
                            double sumsig = 0;
                            for (int n = 0; n < 60; n++)
                            {
                                double t = n * dt;
                                signal[n] = (t - offset / 2) * Math.Pow(Math.Sin(kk * 343 * t), param);
                                sumsig += signal[n] * signal[n];
                            }
                            for (int i = 0; i < signal.Length; i++) signal[i] = Math.Sqrt(signal[i] * signal[i] / sumsig) * ((signal[i] < 0) ? -1 : 1);

                            break;
                        case Signal_Type.SteadyState_Noise:
                            for (int n = 0; n < tmax / dt; n++) signal[n] = R.NextDouble();
                            break;
                        case Signal_Type.SS_Noise_Pulse:
                            for (int n = 0; n < tmax / dt; n++) signal[n] = Math.Exp(-.5 * Math.Pow((double)n / w, 2) * R.NextDouble());
                            break;
                    }
                }

                public void Drive(int t)
                {
                    foreach (Acoustic_Compact_FDTD.Node n in SrcNode)
                    {
                        if (t < signal.Length)
                        {
                            n.Pn = signal[t];
                        }
                        else n.Pn = 0;
                    }
                }

                public System.Numerics.Complex[] Frequency_Response(int length)
                {
                    if (signal.Length > length) length = signal.Length;
                    double[] signal_clone = new double[length];
                    Array.Copy(signal, signal_clone, signal.Length);
                    return Audio.Pach_SP.FFT_General(signal_clone, 0);
                }

                public double frequency
                {
                    get
                    {
                        return f;
                    }
                    set
                    {
                        f = value;
                        Generate_Signal();
                    }
                }
            }

            public class Microphone_Compact
            {
                double[][] recording;
                List<double[][]> storage = new List<double[][]>();
                Acoustic_Compact_FDTD.Node[] RecNode;
                Hare.Geometry.Point[] Loc;
                double tmax;
                int no_of_samples;

                public Microphone_Compact(Hare.Geometry.Point[] Loc_in)
                {
                    Loc = Loc_in;
                    Random R = new Random();
                    recording = new double[Loc.Length][];
                    RecNode = new Acoustic_Compact_FDTD.Node[Loc.Length];
                }

                public void Connect_Grid(Acoustic_Compact_FDTD.Node[][][] Frame, AABB Bounds, double dx, double _tmax, double dt, int no_of_Layers)
                {
                    no_of_samples = (int)Math.Ceiling(_tmax/1000 / dt);
                    tmax = _tmax;

                    for (int i = 0; i < Loc.Length; i++)
                    {
                        recording[i] = new double[no_of_samples];
                        int X = (int)Math.Floor((Loc[i].x - Bounds.Min_PT.x) * Utilities.Numerics.rt2 / (2 * dx)) + no_of_Layers;
                        int Y = (int)Math.Floor((Loc[i].y - Bounds.Min_PT.y) / (2 * dx)) + no_of_Layers;
                        int Z = (int)Math.Floor((Loc[i].z - Bounds.Min_PT.z) / (2 * dx)) + no_of_Layers;
                        RecNode[i] = Frame[X][Y][Z];
                    }
                }

                public void Record(int n)
                {
                    for (int i = 0; i < recording.Length; i++)
                    {
                        recording[i][n] = RecNode[i].P;
                    }
                }

                public void reset()
                {
                    storage.Add(recording);
                    recording = new double[Loc.Length][];
                    for (int i = 0; i < Loc.Length; i++)
                    {
                        recording[i] = new double[no_of_samples];
                    }
                }

                public List<double[][]> Recordings
                {
                    get { return storage; }
                }
            }
        }
    }
}