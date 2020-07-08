﻿namespace Aardvark.Rendering.PointSet

open FSharp.Data.Adaptive
open Aardvark.Base
open Aardvark.Base.Rendering
open Aardvark.SceneGraph

[<ReflectedDefinition>]
module private DeferredPointSetShaders =
    open FShade
    open PointSetShaders

    type PointVertex =
        {
            [<Position>] pos : V4d
            [<Color; Interpolation(InterpolationMode.Flat)>] col : V4d
            //[<Normal>] n : V3d
            [<Semantic("ViewPosition")>] vp : V4d
            [<Semantic("AvgPointDistance")>] dist : float
            [<Semantic("DepthRange"); Interpolation(InterpolationMode.Flat)>] depthRange : float
            [<PointSize>] s : float
            [<Semantic("PointPixelSize")>] ps : float
            [<PointCoord>] c : V2d
            [<Normal>] n : V3d
            [<Semantic("TreeId")>] id : int
            [<Semantic("MaxTreeDepth")>] treeDepth : int
            [<Semantic("Normal32"); Interpolation(InterpolationMode.Flat)>] n32 : int
            [<FragCoord>] fc : V4d
            [<SamplePosition>] sp : V2d
        }
        
    [<Inline>]
    let div (v : V4d) = v.XYZ / v.W
    
    let colorOrWhite (v : Effects.Vertex) =
        vertex {  
            let mutable color = v.c
            if not uniform?ShowColors then color <- V4d.IIII
            return { v with c = color }
        }

    let lodPointSize (v : PointVertex) =
        vertex { 
            let mv = uniform.ModelViewTrafos.[v.id]

            let vp = mv * v.pos
            //let vn = mv * V4d(v.n, 0.0) |> Vec.xyz |> Vec.normalize

            let pp = uniform.ProjTrafo * vp

            let size = uniform.PointSize
            let r = size / V2d uniform.ViewportSize

            let dx = div (vp + uniform.ProjTrafoInv.C0 * r.X * pp.W) - vp.XYZ |> Vec.length
            let dy = div (vp + uniform.ProjTrafoInv.C1 * r.Y * pp.W) - vp.XYZ |> Vec.length
            let dist = 0.5 * (dx + dy)

            let pp0z = pp.Z / pp.W
            let ppz = pp + uniform.ProjTrafo.C2*dist |> div

            let depthRange = abs (pp0z - ppz.Z)


            let col = v.col.XYZ

            let o = uniform.Overlay.[v.id].X
            let col = 
                if o > 0.0 then
                    let h = heat (float v.treeDepth / 6.0)
                    o * h.XYZ + (1.0 - o) * col
                else
                    v.col.XYZ

            return 
                { v with 
                    ps = 1.0
                    s = 1.0
                    pos = pp / pp.W
                    depthRange = depthRange
                    vp = vp 
                    col = V4d(col, v.col.W) 
                }
        }

    let lodPointCircular (v : PointVertex) =
        fragment {
            return { v with pos = V4d(v.pos.XYZ, v.depthRange) }
        }

        

    type FillSphereVertex =
        {
            [<FragCoord>]
            fc : V4d
        }
        

    let cSam =
        sampler2d {
            texture uniform?Colors
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }
        
    let depthSam =
        sampler2d {
            texture uniform?Depth
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

    let ppSam =
        sampler2d {
            texture uniform?Positions
            filter Filter.MinMagPoint
            addressU WrapMode.Clamp
            addressV WrapMode.Clamp
        }

        

    type BlitFragment =
        {
            [<Color>] c : V4d
            [<Depth>] d : float
        }

    let blit (v : FillSphereVertex) =
        fragment {
            let size = V2d uniform.ViewportSize
            let invSize = 1.0 / size
            let tc0 = v.fc.XY * invSize
            let c = cSam.SampleLevel(tc0, 0.0).XYZ
            return { c = V4d(c, 1.0); d = 0.0 }
            
        }

    let fillSpheres (v : FillSphereVertex) =
        let dSam = ()

        fragment {
            let size = V2d uniform.ViewportSize
            let invSize = 1.0 / size
            let tc0 = v.fc.XY * invSize
            let s : float = uniform?PointSize
            let hs = ceil (s / 2.0 + 0.5) |> int

            let rmax2 = sqr (s / 2.0)
            let rmax22 = sqr (s / 2.0 + 1.0)

            let step = V2d.II * invSize

            let mutable minDepth = 2.0
            let mutable minTc = V2d.Zero

            for xo in -hs .. hs do
                for yo in -hs .. hs do
                    let r2 = sqr xo + sqr yo |> float
                    if r2 <= rmax22 then
                        let tc = tc0 + V2d(xo, yo) * step

                        let pCenter = ppSam.SampleLevel(tc, 0.0)
                        if pCenter.Z < 1.0 then
                            let cc = 0.5 * (pCenter.XY + V2d.II) * size
                            let rr = Vec.lengthSquared (cc.XY - v.fc.XY)
                            if rr <= rmax2 then
                                let z = sqrt (1.0 - rr / rmax2)

                                let d = if pCenter.W >= 1.0 then pCenter.Z else pCenter.Z - z * pCenter.W
                                if d < minDepth then
                                    minDepth <- d
                                    minTc <- tc

            if minDepth > 1.0  then
                return { c = cSam.SampleLevel(tc0, 0.0); d = 1.0 }
            else 
                return { c = cSam.SampleLevel(minTc, 0.0); d = 0.5 * minDepth + 0.5 }
                    
        }




    let realRootsOfNormed (c2 : float) (c1 : float) (c0 : float) =
        let mutable d = c2 * c2
        let p3 = 1.0/3.0 * (-1.0/3.0 * d + c1)
        let q2 = 1.0/2.0 * ((2.0/27.0 * d - 1.0/3.0 * c1) * c2 + c0)
        let p3c = p3 * p3 * p3
        let shift = 1.0/3.0 * c2
        d <- q2 * q2 + p3c
        if d < 0.0 then
            let phi = 1.0 / 3.0 * acos (-q2 / sqrt(-p3c));
            let t = 2.0 * sqrt(-p3);
            let r0 = t * cos phi - shift;
            let r1 = -t * cos (phi + Constant.Pi / 3.0) - shift;
            let r2 = -t * cos (phi - Constant.Pi / 3.0) - shift;
            min r0 (min r1 r2)
        
        else
            d <- sqrt d
            let uav = cbrt (d - q2) - cbrt (d + q2)
            let s0 = uav - shift
            let s1 = -0.5 * uav - shift
            min s0 s1

    let samples32 = 
        [|
            //V2d( 0.0, 0.0 )
            V2d( -0.1282280447374989, 0.9832676769841681 )
            V2d( -0.16837061570862488, -0.9830604356268391 )
            V2d( -0.9798310993397246, -0.18741003201467368 )
            V2d( 0.7472985137846405, -0.6138536654225581 )
            V2d( 0.8310405456198345, 0.4374716275485648 )
            V2d( -0.7502439994363835, 0.5809097186211367 )
            V2d( -0.6932459550231614, -0.6776730258773993 )
            V2d( 0.2553124458770123, 0.4959210565172609 )
            V2d( 0.5465954597348052, -0.04472223662487038 )
            V2d( 0.09277897488068058, -0.5113610008959538 )
            V2d( -0.47472312402622696, -0.15862786550784555 )
            V2d( -0.266310498212525, 0.44196009161163013 )
            V2d( 0.977150215545575, -0.07917613624521679 )
            V2d( 0.49363631849664313, 0.8522020775091785 )
            V2d( -0.29941898954428015, -0.6401691156248741 )
            V2d( 0.3093408037469358, -0.8927849883782302 )
            V2d( -0.9050970007350029, 0.2629623257288251 )
            V2d( -0.5427419881280113, 0.20269063268927187 )
            V2d( -0.4213485328487087, 0.8280808387907443 )
            V2d( 0.4260665935850835, -0.4674957666878758 )
            V2d( 0.17217971485841713, 0.8782752290069882 )
            V2d( 0.4956360320348418, 0.32862186679234867 )
            V2d( -0.18798732300077567, -0.30990045623759255 )
            V2d( -0.06471963785060358, 0.661824279105518 )
            V2d( 0.2889479496263221, 0.11796711369520718 )
            V2d( -0.2614045619541214, 0.119872960309031 )
            V2d( 0.7588203318614408, -0.31438846347597377 )
            V2d( 0.03110759701461603, 0.2698364107850206 )
            V2d( -0.7329726419319801, -0.39620420911822984 )
            V2d( -0.7234500503415809, -0.02221612481994348 )
            V2d( 0.5621082888631382, 0.5854406482477675 )
        
        |]

    let samples24 =
        [|
            //V2d( 0.0, 0.0 )
            V2d( -0.4612850228120782, -0.8824263018037591 )
            V2d( 0.2033539719528926, 0.9766070232577696 )
            V2d( 0.8622755945065503, -0.4990552917715807 )
            V2d( -0.8458406529500018, 0.4340626564690164 )
            V2d( 0.9145341241356336, 0.40187426079092753 )
            V2d( -0.8095919285224212, -0.2476471278659192 )
            V2d( 0.2443597793708885, -0.8210571365841042 )
            V2d( -0.29522102954593127, 0.6411496844366571 )
            V2d( 0.4013698454531175, 0.47134750051312063 )
            V2d( -0.1573158341083741, -0.48548502348882533 )
            V2d( 0.5674301785250454, -0.1052346781436156 )
            V2d( -0.4929375319230899, 0.09422383038685558 )
            V2d( 0.967785465127825, -0.06868225365333279 )
            V2d( 0.2267967507441493, -0.40237871966279687 )
            V2d( -0.7200979001122771, -0.6248240905561527 )
            V2d( -0.015195608523765971, 0.35623701723070667 )
            V2d( -0.11428925675805125, -0.963723441683084 )
            V2d( 0.5482105069441386, 0.781847612911249 )
            V2d( -0.6515264455787967, 0.7473765703131305 )
            V2d( 0.5826875031269089, -0.6956573112908789 )
            V2d( -0.8496230198638387, 0.09209564840857346 )
            V2d( 0.38289808661249414, 0.15269522898022844 )
            V2d( -0.4951171173546325, -0.2654758742352245 )
        |]

    let samples16 =
        [|
            //V2d( 0.0, 0.0 )
            V2d( 0.7361771670747784, -0.6743709190503513 )
            V2d( -0.9845834321368149, -0.15755179290200677 )
            V2d( 0.7700310258512124, 0.615334433294869 )
            V2d( -0.27028343507264774, 0.9515396198187963 )
            V2d( -0.2083205670375388, -0.9493326049649131 )
            V2d( 0.9942225629687943, -0.012592138144431327 )
            V2d( -0.6809634346402852, 0.4404622010597125 )
            V2d( 0.18128290701712424, 0.6041707799950865 )
            V2d( -0.5045588564035041, -0.46733843194529184 )
            V2d( 0.2599379562773085, -0.4573765455782192 )
            V2d( 0.47958155225071786, 0.21184103802949944 )
            V2d( -0.5566981555829099, -0.06497638287046927 )
            V2d( 0.33011199190656254, -0.9042445608311466 )
            V2d( -0.2588703212887866, 0.3487611093360376 )
            V2d( 0.5936641948988021, -0.19746322908552932 )
        |]

    type UniformScope with
        member x.NearFar : V2d = uniform?NearFar
        member x.PlaneFit : bool = uniform?PlaneFit
        member x.PlaneFitTolerance : float = uniform?PlaneFitTolerance // 0.05
        member x.PlaneFitRadius : float = uniform?PlaneFitRadius // 7.0
        member x.Gamma : float = uniform?Gamma
        

    [<Inline>]
    let sampleDepth (tc : V2d) =
        depthSam.SampleLevel(tc, 0.0).X * 2.0 - 1.0

    [<Inline>]
    let viewPosSize() =
        V2d depthSam.Size

    let sampleViewPos (tc : V2d) =
        let z = sampleDepth tc
        if z >= 1.0 then
            V3d.Zero
        else
            let ndc = 2.0 * tc - 1.0
            let vp = uniform.ProjTrafoInv * V4d(ndc, z, 1.0)
            vp.XYZ / vp.W

    [<Inline>]
    let sampleSimpleNormal (vp : V3d) (tc : V2d) =
        let s = viewPosSize()
        let vpx = sampleViewPos(tc + V2d.IO / s)
        let vpy = sampleViewPos(tc + V2d.OI / s)
        let vnx = sampleViewPos(tc - V2d.IO / s)
        let vny = sampleViewPos(tc - V2d.OI / s)
            
        let z = abs vp.Z < 0.0001
        let zx = abs vpx.Z < 0.0001
        let zy = abs vpy.Z < 0.0001
        let nx = abs vpx.Z < 0.0001
        let ny = abs vpy.Z < 0.0001

        if z || (zx && nx) || (zy && ny) then 
            -Vec.normalize vp
        //elif zx || zy || z || abs(vp.Z - vpx.Z) > 0.1 || abs(vp.Z - vpy.Z) > 0.1 then
        //    V3d.Zero
        elif not zx && not zy then
            let n = Vec.cross (vpx - vp) (vpy - vp)
            Vec.normalize n
        elif not zx && not ny then
            let n = Vec.cross (vpx - vp) (vp - vny)
            Vec.normalize n
        elif not nx && not zy then
            let n = Vec.cross (vp - vnx) (vpy - vp)
            Vec.normalize n
        else
            let n = Vec.cross (vp - vnx) (vp - vny)
            Vec.normalize n
    
    let randomSam =
        sampler2d {
            texture uniform?RandomTexture
            filter Filter.MinMagPoint
            addressU WrapMode.Wrap
            addressV WrapMode.Wrap
        }

    let sampleNormal (vp : V3d) (tc : V2d) =
        let nf = uniform.NearFar
        let ld = -vp.Z

        if ld > 0.0 && ld < nf.Y && uniform.PlaneFit then
            let vn = sampleSimpleNormal vp tc 
            if vn = V3d.Zero then   
                V4d(vn, vp.Z)
            else
                let size = viewPosSize()

                let plane = V4d(vn, -Vec.dot vn vp)

                let mutable sum = V3d.Zero
                let mutable sumSq = V3d.Zero
                let mutable off = V3d.Zero
                let mutable cnt = 1

                let x = randomSam.SampleLevel((floor (tc * viewPosSize()) + V2d.Half) / V2d randomSam.Size, 0.0).XY |> Vec.normalize
                let y = V2d(-x.Y, x.X)

                for o in samples24 do
                    let tc = tc + uniform.PlaneFitRadius * (x*o.X + y*o.Y) / size

                    let p = sampleViewPos tc
                    if p.Z <> 0.0 && abs (Vec.dot plane (V4d(p, 1.0))) <= uniform.PlaneFitTolerance then
                        let pt = p - vp
                        sum <- sum + pt
                        sumSq <- sumSq + sqr pt
                        off <- off + V3d(pt.Y*pt.Z, pt.X*pt.Z, pt.X*pt.Y)
                        cnt <- cnt + 1

                if cnt >= 3 then

                    let n = float cnt
                    let avg = sum / n
                    let xx = (sumSq.X - avg.X * sum.X) / (n - 1.0)
                    let yy = (sumSq.Y - avg.Y * sum.Y) / (n - 1.0)
                    let zz = (sumSq.Z - avg.Z * sum.Z) / (n - 1.0)

                    let xy = (off.Z - avg.X * sum.Y) / (n - 1.0)
                    let xz = (off.Y - avg.X * sum.Z) / (n - 1.0)
                    let yz = (off.X - avg.Y * sum.Z) / (n - 1.0)
            
                    let _a = 1.0
                    let b = -xx - yy - zz
                    let c = -sqr xy - sqr xz - sqr yz + xx*yy + xx*zz + yy*zz
                    let d = -xx*yy*zz - 2.0*xy*xz*yz + sqr xz*yy + sqr xy*zz + sqr yz*xx


                    let l = realRootsOfNormed b c d

                    let c0 = V3d(xx - l, xy, xz)
                    let c1 = V3d(xy, yy - l, yz)
                    let c2 = V3d(xz, yz, zz - l)
                    let len0 = Vec.lengthSquared c0
                    let len1 = Vec.lengthSquared c1
                    let len2 = Vec.lengthSquared c2

                    let normal =
                        if len0 > len1 then
                            if len2 > len1 then Vec.cross c0 c2 |> Vec.normalize
                            else Vec.cross c0 c1 |> Vec.normalize
                        else
                            if len2 > len0 then Vec.cross c1 c2 |> Vec.normalize
                            else Vec.cross c0 c1 |> Vec.normalize
                    V4d(normal, -Vec.dot normal (vp + avg))
                else
                    V4d(vn, -Vec.dot vn vp)


        else
            let vn = sampleSimpleNormal vp tc
            V4d(vn, -Vec.dot vn vp)




    type Fragment =
        {
            [<Color>]
            color : V4d
            
            [<Normal>]
            normal : V3d

            [<Depth>]
            depth : float
        }

    let blitPlaneFit (v : Effects.Vertex) =
        fragment {
            let z = sampleDepth v.tc
            if z < -1.0 || z >= 1.0 then discard()
            let vp = sampleViewPos v.tc

            let c = cSam.SampleLevel(v.tc, 0.0).XYZ
            let plane = sampleNormal vp v.tc
            let n = plane.XYZ

            let diffuse = 
                if uniform?Diffuse then (Vec.dot (Vec.normalize (vp.XYZ - V3d.Zero)) n) |> abs
                else 1.0

            let col = c.XYZ * (0.2 + 0.8*diffuse)
            let col = col ** (1.0 / uniform.Gamma)

            let mutable finalDepth = 2.0

            if n.Z <> 0.0 then
                // l*<vp|n> + w = 0
                let l = -plane.W / Vec.dot vp.XYZ n
                let npos = vp.XYZ * l
                let pp = uniform.ProjTrafo * V4d(npos, 1.0)
                finalDepth <- pp.Z / pp.W
            else
                finalDepth <- sampleDepth v.tc

            return {
                color = V4d(col, 1.0)
                normal = n
                depth = finalDepth * 0.5 + 0.5
            }
        }


type PointSetRenderConfig =
    {
        runtime         : IRuntime
        size            : aval<V2i>
        viewTrafo       : aval<Trafo3d>
        projTrafo       : aval<Trafo3d>

        colors          : aval<bool>
        pointSize       : aval<float>
        planeFit        : aval<bool>
        diffuse         : aval<bool>
        ssao            : aval<bool>
        planeFitTol     : aval<float>
        planeFitRadius  : aval<float>
        gamma           : aval<float>

        lodConfig       : LodTreeRenderConfig

    }

module Sg =
    let pointSets (config : PointSetRenderConfig) (pointClouds : aset<LodTreeInstance>) =
        let runtime = config.runtime

        let textures = 
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, RenderbufferFormat.Rgba8
                    DefaultSemantic.Positions, RenderbufferFormat.Rgba32f
                    DefaultSemantic.Depth, RenderbufferFormat.Depth24Stencil8
                ]

            let sems =
                Set.ofList [
                    DefaultSemantic.Positions; DefaultSemantic.Colors; DefaultSemantic.Depth
                ]

            let render = 
                Sg.lodTree config.lodConfig pointClouds
                |> Sg.shader {
                    do! DeferredPointSetShaders.colorOrWhite
                    do! DeferredPointSetShaders.lodPointSize
                    do! DeferredPointSetShaders.lodPointCircular
                }
                |> Sg.blendMode (AVal.constant BlendMode.None)
                |> Sg.uniform "ShowColors" config.colors
                |> Sg.uniform "PointSize" config.pointSize
                |> Sg.uniform "ViewportSize" config.size
                |> Sg.viewTrafo config.viewTrafo
                |> Sg.projTrafo config.projTrafo
                |> Sg.compile runtime signature

            let clear =
                runtime.CompileClear(signature, AVal.constant (Map.ofList [DefaultSemantic.Positions, C4f(0.0f, 0.0f, 2.0f, 0.0f)]), AVal.constant (Some 1.0))

            RenderTask.ofList [clear; render]
            |> RenderTask.renderSemantics sems config.size

        
        let color = textures.[DefaultSemantic.Colors]
        let position = textures.[DefaultSemantic.Positions]
        let depth = textures.[DefaultSemantic.Depth]


        let sphereTextures =
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, RenderbufferFormat.Rgba8
                    DefaultSemantic.Depth, RenderbufferFormat.Depth24Stencil8
                ]
            let sems =
                Set.ofList [
                    DefaultSemantic.Colors; DefaultSemantic.Depth
                ]
            Sg.fullScreenQuad
            |> Sg.shader {
                do! DeferredPointSetShaders.fillSpheres
            }
            |> Sg.uniform "PointSize" config.pointSize
            |> Sg.uniform "ViewportSize" config.size
            |> Sg.texture DefaultSemantic.Positions position
            |> Sg.texture DefaultSemantic.Colors color
            |> Sg.texture DefaultSemantic.Depth depth
            |> Sg.viewTrafo config.viewTrafo
            |> Sg.projTrafo config.projTrafo
            |> Sg.compile runtime signature
            |> RenderTask.renderSemantics sems config.size


        let color = sphereTextures.[DefaultSemantic.Colors]
        let depth = sphereTextures.[DefaultSemantic.Depth]

        let nearFar =
            config.projTrafo |> AVal.map (fun t ->
                let f = Frustum.ofTrafo t
                V2d(f.near, f.far)
            )


        let randomTex = 
            let img = PixImage<float32>(Col.Format.RGB, V2i.II * 512)

            let rand = RandomSystem()
            img.GetMatrix<C3f>().SetByCoord (fun _ ->
                V3d(rand.UniformV2dDirection(), 0.0).ToC3d().ToC3f()
            ) |> ignore

            runtime.PrepareTexture(PixTexture2d(PixImageMipMap [| img :> PixImage |], TextureParams.empty)) :> ITexture
            
        let sceneTextures =
            let signature =
                runtime.CreateFramebufferSignature [
                    DefaultSemantic.Colors, RenderbufferFormat.Rgba8
                    DefaultSemantic.Normals, RenderbufferFormat.Rgb32f
                    DefaultSemantic.Depth, RenderbufferFormat.Depth24Stencil8
                ]

            let sems =
                Set.ofList [
                    DefaultSemantic.Colors; DefaultSemantic.Normals; DefaultSemantic.Depth
                ]

            Sg.fullScreenQuad
            |> Sg.texture DefaultSemantic.Colors color
            |> Sg.texture DefaultSemantic.Depth depth
            |> Sg.uniform "NearFar" nearFar
            |> Sg.uniform "PlaneFit" config.planeFit
            |> Sg.uniform "SSAO" config.ssao
            |> Sg.uniform "RandomTexture" (AVal.constant randomTex)
            |> Sg.shader {
                do! DeferredPointSetShaders.blitPlaneFit
            }
            |> Sg.viewTrafo config.viewTrafo
            |> Sg.projTrafo config.projTrafo
            |> Sg.uniform "Diffuse" config.diffuse
            |> Sg.uniform "ViewportSize" config.size
            |> Sg.uniform "PlaneFitTolerance" config.planeFitTol
            |> Sg.uniform "PlaneFitRadius" config.planeFitRadius
            |> Sg.uniform "Gamma" config.gamma
            |> Sg.compile runtime signature
            |> RenderTask.renderSemantics sems config.size
              
        let normals = sceneTextures.[DefaultSemantic.Normals]
        let colors = sceneTextures.[DefaultSemantic.Colors]
        let depth = sceneTextures.[DefaultSemantic.Depth]

            
        let finalSg =
            let cfg =
                {
                    radius = AVal.constant 0.04
                    threshold = AVal.constant 0.8
                    sigma = AVal.constant 3.0
                    sharpness = AVal.constant 1.0
                    samples = AVal.constant 16
                }

            let s = config.size |> AVal.map (fun s -> max V2i.II (s / 2))

            SSAO.getAmbient config.ssao cfg runtime config.projTrafo depth normals colors s
            |> Sg.uniform "SSAO" config.ssao
            |> Sg.uniform "ViewportSize" config.size

        finalSg

