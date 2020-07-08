(*
    Copied from https://github.com/aardvark-community/hum.
*)
namespace Aardvark.Algodat.App.Viewer

open FSharp.Data.Adaptive
open FSharp.Data.Adaptive.Operators
open Aardvark.Base
open Aardvark.SceneGraph
open Aardvark.Application
open Aardvark.Application.Slim
open Aardvark.Rendering.Text
open Aardvark.Rendering.PointSet
open Aardvark.Base.Rendering
open Aardvark.Base.Geometry
open Aardvark.Geometry.Points
open FShade

module Util =

    module Shader =
        open FShade

        let reverseTrafo (v : Effects.Vertex) =
            vertex {
                let wp = uniform.ViewProjTrafoInv * v.pos
                return { v with wp = wp / wp.W }
            }
    
        let hi = 70.0 / 255.0
        let lo = 30.0 / 255.0
        let qf = (V4d(hi,lo,lo,1.0))
        let qb = (V4d(lo,hi,hi,1.0))
        let ql = (V4d(lo,hi,lo,1.0))
        let qr = (V4d(hi,lo,hi,1.0))
        let qu = (V4d(lo,lo,hi,1.0))
        let qd = (V4d(hi,hi,lo,1.0))

        let box (v : Effects.Vertex) =
            fragment {
                let c = uniform.CameraLocation
                let f = v.wp.XYZ
                let dir = Vec.normalize (f - c)
                
                let absDir = V3d(abs dir.X, abs dir.Y, abs dir.Z)

                if absDir.X > absDir.Y && absDir.X > absDir.Z then 
                    if dir.X > 0.0 then return qf
                    else return qb
                elif absDir.Y > absDir.X && absDir.Y > absDir.Z then
                    if dir.Y > 0.0 then return ql
                    else return qr
                else
                    if dir.Z > 0.0 then return qu
                    else return qd

            }

        let env =
            samplerCube {
                texture uniform?EnvMap
                addressU WrapMode.Wrap
                addressV WrapMode.Wrap
                addressW WrapMode.Wrap
                filter Filter.MinMagMipLinear
            }

        let envMap (v : Effects.Vertex) =
            fragment {
                
                let vp = uniform.ProjTrafoInv * V4d(v.pos.X, v.pos.Y, -1.0, 1.0)
                let vp = vp.XYZ / vp.W

                let dir = (uniform.ViewTrafoInv * V4d(vp, 0.0)).XYZ |> Vec.normalize


                //let wp = uniform.ViewProjTrafoInv * V4d(v.pos.X, v.pos.Y, -1.0, 1.0)

                //let f = 1.0 / (uniform.ViewProjTrafoInv.M33 - uniform.ViewProjTrafoInv.M32)
                
                //let dir = 
                //    f * uniform.ViewProjTrafoInv.C0.XYZ * v.pos.X + 
                //    f * uniform.ViewProjTrafoInv.C1.XYZ * v.pos.Y +
                //    f * uniform.ViewProjTrafoInv.C2.XYZ * -1.0 +

                //    f * uniform.ViewProjTrafoInv.C3.XYZ +
                //    (uniform.ViewProjTrafoInv.C2.XYZ) / (-uniform.ViewProjTrafoInv.M32)

                //let dir = Vec.normalize dir

                //let c = uniform.CameraLocation
                //let f = v.wp.XYZ
                //let dir = Vec.normalize (f - c)
                return env.Sample(dir)
            }

    let coordinateBox =
        Sg.farPlaneQuad
            |> Sg.shader  {
                do! Shader.reverseTrafo
                do! Shader.box
            }
    

module Rendering =


    let pointClouds (win : IRenderWindow) (msaa : bool) (camera : aval<CameraView>) (frustum : aval<Frustum>) (pcs : list<LodTreeInstance>) =
        let picktrees : cmap<ILodTreeNode,SimplePickTree> = cmap()
        let config =
            {
                pointSize = AVal.init 1.0
                overlayAlpha = AVal.init 0.0
                maxSplits = AVal.init 8
                renderBounds = AVal.init false
                sort = AVal.init false
                splitfactor = AVal.init 0.45
                budget = AVal.init -(256L <<< 10)
                lighting = AVal.init true
                colors = AVal.init true
                gamma = AVal.init 1.0
                stats = AVal.init Unchecked.defaultof<_>
                background = AVal.init (Background.Skybox Skybox.Miramar)
                ssao = AVal.init false
                planeFit = AVal.init false
            }



        let pcs =
            pcs |> List.map (fun t ->
                { t with uniforms = MapExt.add "Overlay" (config.overlayAlpha |> AVal.map ((*) V4d.IIII) :> IAdaptiveValue) t.uniforms }
            )
        
        let trafo = Trafo3d.Identity
            //let bb = 
            //    pcs |> List.map (fun i -> 
            //        match i.root with
            //        | :? LodTreeInstance.PointTreeNode as n -> n.Original.BoundingBoxApproximate
            //        | _ -> i.root.WorldBoundingBox
            //    ) |> Box3d
            //Trafo3d.Translation(-bb.Center) * 
            //Trafo3d.Scale(300.0 / bb.Size.NormMax) *
            //Trafo3d.Translation(bb.Center)

        let pcs =
            pcs |> List.toArray |> Array.map (LodTreeInstance.transform trafo >> AVal.init)
            
        let cfg =
            RenderConfig.toSg win config


        let v = (camera |> AVal.map CameraView.viewTrafo)
        let p = (frustum |> AVal.map Frustum.projTrafo)

        let reset = AVal.init 0 
        //let filter : ModRef<Option<Hull3d>> = AVal.init None

        let instances = 
            aset {
                if pcs.Length > 0 then
                    let! i = reset
                    let! pc = pcs.[i%pcs.Length]
                    yield pc

            }

        let renderConfig : PointSetRenderConfig =
            {
                runtime = win.Runtime
                viewTrafo = v
                projTrafo = p
                size = win.Sizes
                colors = config.colors
                pointSize = config.pointSize |> AVal.map ((*) 10.0)
                planeFit = config.planeFit
                planeFitTol = AVal.constant 0.01
                planeFitRadius = AVal.constant 7.0
                ssao = config.ssao
                diffuse = config.lighting
                gamma = config.gamma
                lodConfig =
                    {
                        time = win.Time
                        renderBounds = config.renderBounds
                        stats = config.stats
                        pickTrees = Some picktrees
                        alphaToCoverage = false
                        maxSplits = config.maxSplits
                        splitfactor = config.splitfactor
                        budget = config.budget
                    }
            }

        let sg =
            Sg.pointSets renderConfig instances
            |> Sg.andAlso cfg
            |> Sg.uniform "ViewportSize" win.Sizes
            //Sg.LodTreeNode(config.stats, picktrees, true, config.budget, config.splitfactor, config.renderBounds, config.maxSplits, win.Time, instances) :> ISg
            //|> Sg.uniform "PointSize" config.pointSize
            //|> Sg.uniform "ViewportSize" win.Sizes
            //|> Sg.uniform "PointVisualization" vis
            //|> Sg.uniform "MagicExp" config.magicExp
            //|> Sg.shader {
            //    //do! fun (v : PointSetShaders.PointVertex) -> vertex { return { v with col = V4d.IIII } }
            //    do! PointSetShaders.lodPointSize
            //    //do! PointSetShaders.cameraLight
            //    //if msaa then
            //    //    do! PointSetShaders.lodPointCircularMSAA
            //    //else
            //    do! PointSetShaders.lodPointCircular
            //    //do! PointSetShaders.envMap
            //}
            ////|> Sg.andAlso thing
            //|> Sg.multisample (AVal.constant true)
            //|> Sg.viewTrafo v
            //|> Sg.projTrafo p
            //|> Sg.andAlso cfg
            ////|> Sg.andAlso bla
            //|> Sg.blendMode (AVal.constant BlendMode.None)


        let switchActive = win.Keyboard.IsDown Keys.M
        let switchThread =
            for i in 1 .. 1 do
                startThread (fun () ->
                    let rand = RandomSystem()
                    while true do
                        System.Threading.Thread.Sleep(rand.UniformInt(100))
                        if AVal.force switchActive then
                            transact (fun () -> reset.Value <- reset.Value + 1)
                    
                ) |> ignore


        win.Keyboard.DownWithRepeats.Values.Add(fun k ->
            match k with

            | Keys.Delete ->
                let i = pcs.[0].Value
                match i.root with
                | :? LodTreeInstance.PointTreeNode as n -> 
                    match n.Delete (Box3d.FromCenterAndSize(i.root.WorldBoundingBox.Center, V3d(10.0, 10.0, 1000.0))) with
                    | Some r ->
                        transact (fun () -> 
                            pcs.[0].Value <- { i with root = r }
                        )
                    | None ->
                        ()
                | _ ->
                    ()


            | Keys.Escape ->
                transact (fun () ->
                    let pc = pcs.[0].Value
                    let bb = pc.root.WorldBoundingBox
                    let f1 = Hull3d.Create (Box3d.FromMinAndSize(bb.Min, bb.Size * V3d(1.0, 1.0, 0.15)))

                    let root = pc.root |> unbox<LodTreeInstance.PointTreeNode>
                    match root.Original with
                    | :? FilteredNode as fn ->
                        let inner = fn.Node
                        Log.warn "%A" inner
                        match root.WithPointCloudNode(inner) with
                        | Some pp ->
                            pcs.[0].Value <- { pc with root = pp }
                        | None ->
                            Log.warn "hinig"
                    | _ -> 
                        match LodTreeInstance.filter (FilterInsideConvexHull3d f1) pc with
                        | Some pc -> 
                            pcs.[0].Value <- pc
                        | None -> 
                            Log.warn "hinig"
                  
                )
            | Keys.I ->
                transact (fun () -> config.gamma.Value <- min 4.0 (config.gamma.Value + 0.1))
            | Keys.U ->
                transact (fun () -> config.gamma.Value <- max 0.0 (config.gamma.Value - 0.1))
            | Keys.V ->
                transact (fun () ->
                    config.colors.Value <- not config.colors.Value
                )



            | Keys.L ->
                transact (fun () ->
                    config.lighting.Value <- not config.lighting.Value
                )
            //| Keys.M -> 
            //    transact ( fun () -> reset.Value <- reset.Value + 1 )

            | Keys.O -> transact (fun () -> config.pointSize.Value <- config.pointSize.Value / 1.3)
            | Keys.P -> transact (fun () -> config.pointSize.Value <- config.pointSize.Value * 1.3)
            | Keys.Subtract | Keys.OemMinus -> transact (fun () -> config.overlayAlpha.Value <- max 0.0 (config.overlayAlpha.Value - 0.1))
            | Keys.Add | Keys.OemPlus -> transact (fun () -> config.overlayAlpha.Value <- min 1.0 (config.overlayAlpha.Value + 0.1))
        
            //| Keys.Up -> transact (fun () -> config.maxSplits.Value <- config.maxSplits.Value + 1); printfn "splits: %A" config.maxSplits.Value
            //| Keys.Down -> transact (fun () -> config.maxSplits.Value <- max 1 (config.maxSplits.Value - 1)); printfn "splits: %A" config.maxSplits.Value
            | Keys.F -> transact (fun () -> if config.maxSplits.Value = 0 then printfn "unfreeze"; config.maxSplits.Value <- 12 else printfn "freeze"; config.maxSplits.Value <- 0)
            | Keys.C -> transact (fun () -> if config.budget.Value > 0L && config.budget.Value < (1L <<< 30) then config.budget.Value <- 2L * config.budget.Value); Log.line "budget: %A" config.budget.Value
            | Keys.X -> transact (fun () -> if config.budget.Value > (256L <<< 10) then config.budget.Value <- max (config.budget.Value / 2L) (256L <<< 10)); Log.line "budget: %A" config.budget.Value
        
            | Keys.B -> transact (fun () -> config.renderBounds.Value <- not config.renderBounds.Value); Log.line "bounds: %A" config.renderBounds.Value
            
            | Keys.N -> transact (fun () -> config.sort.Value <- not config.sort.Value); Log.line "sort: %A" config.sort.Value

            | Keys.Y -> transact (fun () -> config.budget.Value <- -config.budget.Value)
            
            | Keys.Space -> 
                transact (fun () -> 
                    match config.background.Value with
                    | Background.Skybox s -> 
                        match s with
                        | Skybox.Miramar -> config.background.Value <- Background.Skybox Skybox.ViolentDays
                        | Skybox.ViolentDays -> config.background.Value <- Background.Skybox Skybox.Wasserleonburg
                        | Skybox.Wasserleonburg -> config.background.Value <- Background.CoordinateBox
                    | Background.CoordinateBox ->  config.background.Value <- Background.Black
                    | Background.Black -> config.background.Value <- Background.Skybox Skybox.Miramar
                )

            | Keys.D1 -> transact (fun () -> config.planeFit.Value <- not config.planeFit.Value)
            | Keys.D2 -> transact (fun () -> config.ssao.Value <- not config.ssao.Value)

            //| Keys.N -> transact (fun () -> reset.Value <- reset.Value + 1)
            | Keys.Return -> Log.line "%A" config.stats.Value

            | k -> 
                ()
        )

        config, sg

    let skybox (name : string) =
        
        AVal.custom (fun _ ->
            let env =
                let trafo t (img : PixImage) = img.Transformed t
                let load (name : string) =
                    use s = typeof<Args>.Assembly.GetManifestResourceStream("Viewer.CubeMap." + name)
                    PixImage.Create(s, PixLoadOptions.Default)
                
                PixImageCube [|
                    PixImageMipMap(
                        load (name.Replace("$", "rt"))
                        |> trafo ImageTrafo.Rot90
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "lf"))
                        |> trafo ImageTrafo.Rot270
                    )
                
                    PixImageMipMap(
                        load (name.Replace("$", "bk"))
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "ft"))
                        |> trafo ImageTrafo.Rot180
                    )
                
                    PixImageMipMap(
                        load (name.Replace("$", "up"))
                        |> trafo ImageTrafo.Rot90
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "dn"))
                        |> trafo ImageTrafo.Rot90
                    )
                |]

            PixTextureCube(env, TextureParams.mipmapped) :> ITexture
        )

    let rftSky =
        let name = "2010.04.29-16.59.11-$.jpg"
        AVal.custom (fun _ ->
            let env =
                let trafo t (img : PixImage) = img.Transformed t
                let load (name : string) =
                    use s = typeof<Args>.Assembly.GetManifestResourceStream("Viewer.CubeMap." + name)
                    PixImage.Create(s, PixLoadOptions.Default)
                
                PixImageCube [|
                    PixImageMipMap(
                        load (name.Replace("$", "l"))
                        |> trafo ImageTrafo.Rot90
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "r"))
                        |> trafo ImageTrafo.Rot270
                    )
                
                    PixImageMipMap(
                        load (name.Replace("$", "b"))
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "f"))
                        |> trafo ImageTrafo.Rot180
                    )
                
                    PixImageMipMap(
                        load (name.Replace("$", "u"))
                        |> trafo ImageTrafo.Rot180
                    )
                    PixImageMipMap(
                        load (name.Replace("$", "d"))
                        |> trafo ImageTrafo.Rot90
                    )
                |]

            PixTextureCube(env, TextureParams.mipmapped) :> ITexture
        )

    let skyboxes =
        Map.ofList [
            Skybox.Miramar, skybox "miramar_$.png"
            Skybox.ViolentDays, skybox "violentdays_$.jpg"
            Skybox.Wasserleonburg, rftSky
        ]


    let show (args : Args) (pcs : list<LodTreeInstance>) =
        Aardvark.Init()

        use app = new OpenGlApplication(true, true)
        use win = app.CreateGameWindow(8)
        

        win.DropFiles.Add(fun e ->
            ()
        )
            
        let bb = 
            pcs |> List.map (fun i -> 
                match i.root with
                | :? LodTreeInstance.PointTreeNode as n -> n.Original.BoundingBoxApproximate
                | _ -> i.root.WorldBoundingBox
            ) |> Box3d

        let loc, center =
            let pc = pcs |> List.head
        
            let rand = RandomSystem()
            match pc.root with
            | :? LodTreeInstance.PointTreeNode as n -> 
                let c = n.Original.Center + V3d n.Original.CentroidLocal
                let pos = c + rand.UniformV3dDirection() * 2.0 * float n.Original.CentroidLocalStdDev
                pos, c
            | _ -> 
                let bb = pc.root.WorldBoundingBox
                bb.Max, bb.Center
        let speed = AVal.init 10.0
        win.Keyboard.DownWithRepeats.Values.Add(function
            | Keys.PageUp | Keys.Up -> transact(fun () -> speed.Value <- speed.Value * 1.5)
            | Keys.PageDown | Keys.Down -> transact(fun () -> speed.Value <- speed.Value / 1.5)
            | _ -> ()
        )

        let camera =
            CameraView.lookAt loc center V3d.OOI
            |> DefaultCameraController.controlWithSpeed speed win.Mouse win.Keyboard win.Time

        //let bb = Box3d.FromCenterAndSize(V3d.Zero, V3d.III * 300.0)

        let frustum =
            AVal.custom (fun t ->
                let s = win.Sizes.GetValue t
                let c = camera.GetValue t

                let (minPt, maxPt) = bb.GetMinMaxInDirection(c.Forward)
                
                let near = Vec.dot c.Forward (minPt - c.Location)
                let far = Vec.dot c.Forward (maxPt - c.Location)
                let near = max (max 0.05 near) (far / 100000.0)

                Frustum.perspective args.fov near far (float s.X / float s.Y)
            )


        let config, pcs = pointClouds win args.msaa camera frustum pcs
        
        let sg =
            Sg.ofList [
                pcs

                Util.coordinateBox
                |> Sg.onOff (config.background |> AVal.map ((=) Background.CoordinateBox))

                Sg.ofList (
                    skyboxes |> Map.toList |> List.map (fun (id, tex) ->
                        Sg.farPlaneQuad
                        |> Sg.uniform "EnvMap" tex
                        |> Sg.onOff (config.background |> AVal.map ((=) (Background.Skybox id)))
                    )
                )
                |> Sg.shader {
                    do! Util.Shader.reverseTrafo
                    do! Util.Shader.envMap
                }

            ]
            |> Sg.viewTrafo (camera |> AVal.map CameraView.viewTrafo)
            |> Sg.projTrafo (frustum |> AVal.map Frustum.projTrafo)
            //|> Sg.uniform "EnvMap" skyboxes.[Skybox.ViolentDays]
    
        win.RenderTask <- Sg.compile app.Runtime win.FramebufferSignature sg
        win.Run()