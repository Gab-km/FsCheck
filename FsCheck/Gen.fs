﻿(*--------------------------------------------------------------------------*\
**  FsCheck                                                                 **
**  Copyright (c) 2008-2010 Kurt Schelfthout. All rights reserved.          **
**  http://www.codeplex.com/fscheck                                         **
**                                                                          **
**  This software is released under the terms of the Revised BSD License.   **
**  See the file License.txt for the full text.                             **
\*--------------------------------------------------------------------------*)

#light

namespace FsCheck

open Random

type internal IGen = 
    abstract AsGenObject : Gen<obj>
    
///Generator of a random value, based on a size parameter and a randomly generated int.
and Gen<'a> = 
    internal Gen of (int -> StdGen -> 'a)
        ///map the given function to the value in the generator, yielding a new generator of the result type.  
        member internal x.Map<'a,'b> (f: 'a -> 'b) : Gen<'b> = match x with (Gen g) -> Gen (fun n r -> f <| g n r)
    interface IGen with
        member x.AsGenObject = x.Map box

//private interface for reflection
type internal IArbitrary =
    abstract ArbitraryObj : Gen<obj>
    abstract ShrinkObj : obj -> seq<obj>   

[<AbstractClass>]
type Arbitrary<'a>() =
    ///Returns a generator for 'a.
    abstract Arbitrary      : Gen<'a>
    ///Returns a generator transformer for 'a. Necessary to generate functions with 'a as domain. Fails by
    ///default if it is not overridden.
    abstract CoArbitrary    : 'a -> (Gen<'c> -> Gen<'c>) 
    ///Returns a sequence of the immediate shrinks of the given value. The immediate shrinks should not include
    ///doubles or the given value itself. The default implementation returns the empty sequence (i.e. no shrinking).
    abstract Shrink         : 'a -> seq<'a>
    default x.CoArbitrary (_:'a) = 
        failwithf "CoArbitrary for %A is not implemented" (typeof<'a>)
    default x.Shrink a = 
        Seq.empty
    interface IArbitrary with
        member x.ArbitraryObj = (x.Arbitrary :> IGen).AsGenObject
        member x.ShrinkObj o = (x.Shrink (unbox o)) |> Seq.map box



[<AutoOpen>]
module GenBuilder =

    ///The workflow type for generators.
    type GenBuilder internal() =
        member b.Return(a) : Gen<_> = 
            Gen (fun n r -> a)
        member b.Bind((Gen m) : Gen<_>, k : _ -> Gen<_>) : Gen<_> = 
            Gen (fun n r0 -> let r1,r2 = split r0
                             let (Gen m') = k (m n r1) 
                             m' n r2)                                      
        member b.Delay(f : unit -> Gen<_>) : Gen<_> = 
            Gen (fun n r -> match f() with (Gen g) -> g n r )
        member b.TryFinally(Gen m,handler ) = 
            Gen (fun n r -> try m n r finally handler)
        member b.TryWith(Gen m, handler) = 
            Gen (fun n r -> try m n r with e -> handler e)
        member b.Using (a, k) =  //'a * ('a -> Gen<'b>) -> Gen<'b> when 'a :> System.IDisposable
            use disposea = a
            k disposea
        member b.ReturnFrom(a:Gen<_>) = a

    ///The workflow function for generators, e.g. gen { ... }
    let gen = GenBuilder()

module Gen =

    open Common
    open Random
    open Reflect
    open System
    open System.Reflection
    open System.Collections.Generic
    open TypeClass

    ///Apply ('map') the function f on the value in the generator, yielding a new generator.
    let map f (gen:Gen<_>) = gen.Map f

    ///Obtain the current size. sized g calls g, passing it the current size as a parameter.
    let sized fgen = Gen (fun n r -> let (Gen m) = fgen n in m n r)

    ///Override the current size of the test. resize n g invokes generator g with size parameter n.
    let resize n (Gen m) = Gen (fun _ r -> m n r)

    ///Default generator that generates a random number generator. Useful for starting off the process
    ///of generating a random value.
    let internal rand = Gen (fun n r -> r)

    ///Generates a value out of the generator with maximum size n.
    let generate n rnd (Gen m) = 
        let size,rnd' = range (0,n) rnd
        m size rnd'

    ///Generates an integer between l and h, inclusive.
    ///Note to QuickCheck users: this function is more general in QuickCheck, generating a Random a.
    let choose (l, h) = rand |> map (range (l,h) >> fst) 

    ///Build a generator that randomly generates one of the values in the given non-empty list.
    let elements xs = 
        choose (0, (Seq.length xs)-1)  |> map(flip Seq.nth xs)

    ///Build a generator that generates a value from one of the generators in the given non-empty list, with
    ///equal probability.
    let oneof gens = gen.Bind(elements gens, id)

    ///Build a generator that generates a value from one of the generators in the given non-empty list, with
    ///given probabilities. The sum of the probabilities must be larger than zero.
    let frequency xs = 
        let tot = Seq.sumBy fst xs
        let rec pick n ys = 
            let (k,x),xs = Seq.head ys,Seq.skip 1 ys
            if n<=k then x else pick (n-k) xs
        in gen.Bind(choose (1,tot), fun n -> pick n xs) 

    ///Map the given function over values to a function over generators of those values.
    let map2 f = fun a b -> gen {   let! a' = a
                                    let! b' = b
                                    return f a' b' }
                                        
    ///Build a generator that generates a 2-tuple of the values generated by the given generator.
    let two g = map2 (fun a b -> (a,b)) g g

    ///Map the given function over values to a function over generators of those values.
    let map3 f = fun a b c -> gen { let! a' = a
                                    let! b' = b
                                    let! c' = c
                                    return f a' b' c' }

    ///Build a generator that generates a 3-tuple of the values generated by the given generator.
    let three g = map3 (fun a b c -> (a,b,c)) g g g

    ///Map the given function over values to a function over generators of those values.
    let map4 f = fun a b c d -> gen {   let! a' = a
                                        let! b' = b
                                        let! c' = c
                                        let! d' = d
                                        return f a' b' c' d' }

    ///Build a generator that generates a 4-tuple of the values generated by the given generator.
    let four g = map4 (fun a b c d -> (a,b,c,d)) g g g g

    ///Map the given function over values to a function over generators of those values.
    let map5 f = fun a b c d e -> gen {  let! a' = a
                                         let! b' = b
                                         let! c' = c
                                         let! d' = d
                                         let! e' = e
                                         return f a' b' c' d' e'}

    ///Map the given function over values to a function over generators of those values.
    let map6 f = fun a b c d e g -> gen {   let! a' = a
                                            let! b' = b
                                            let! c' = c
                                            let! d' = d
                                            let! e' = e
                                            let! g' = g
                                            return f a' b' c' d' e' g'}

    

    
//    let rec sequence l = 
//        match l with
//        | [] -> gen { return [] }
//        | c::cs -> gen {let! x = c
//                        let! xs = sequence cs
//                        return  x::xs }

    ///Sequence the given list of generators into a generator of a list.
    let sequence l = 
        //TODO examine out and try to make a bit prettier - tail recursive version of sequence
        let rec go gs acc size r0 = 
             match gs with
             | [] -> List.rev acc
             | (Gen g)::gs' ->
                let r1,r2 = split r0
                let y = g size r1
                go gs' (y::acc) size r2
        Gen(fun n r -> go l [] n r)

    ///Generates a list of given length, containing values generated by the given generator.
    ///vector g n generates a list of n t's, if t is the type that g generates.
    let vector arb n = sequence [ for i in 1..n -> arb ]

    ///Generates a list of given length, containing values generated by the given generator.
    ///Identical to vector, but with arguments reversed so it is consistent with listOf and nonEmptyListOf.
    let vectorOf n arb = vector arb n

    ///Tries to generate a value that satisfies a predicate. This function 'gives up' by generating None
    ///if the given original generator did not generate any values that satisfied the predicate, after trying to
    ///get values from by increasing its size.
    ///Note to QuickCheck users: order of arguments wrt QuickCheck is reversed, to make piping easier.
    let suchThatOption p gn =
        let rec tryValue k n =
            match (k,n) with 
            | (_,0) -> gen {return None }
            | (k,n) -> gen {let! x = resize (2*k+n) gn
                            if p x then return Some x else return! tryValue (k+1) (n-1) }
        sized (tryValue 0 << max 1)


    ///Generates a value that satisfies a predicate. Contrary to suchThatOption, this function keeps re-trying
    ///by increasing the size of the original generator ad infinitum.  Make sure there is a high chance that 
    ///the predicate is satisfied.
    ///Note to QuickCheck users: order of arguments wrt QuickCheck is reversed, to make piping easier.
    let rec suchThat p gn =
        gen {   let! mx = suchThatOption p gn
                match mx with
                | Some x    -> return x
                | None      -> return! sized (fun n -> resize (n+1) (suchThat p gn)) }

    ///// Takes a list of increasing size, and chooses
    ///// among an initial segment of the list. The size of this initial
    ///// segment increases with the size parameter.
    ///// The input list must be non-empty.
    //let growingElements xs =
    //    match xs with
    //    | [] -> failwith "growingElements used with empty list"
    //    | xs ->
    //        let k = List.length xs |> float
    //        let mx = 100.0
    //        let log' = round << log
    //        let size n = ((log' (float n) + 1.0) * k ) / (log' mx) |> int
    //        sized (fun n -> elements (xs |> Seq.take (max 1 (size n)) |> Seq.toList))
    // TODO check that this does not try choose from segments longer than the original
    // it seems that mx indicates the maximum size that the resulting generator can be called with


    /// Generates a list of random length. The maximum length depends on the
    /// size parameter.
    let listOf gn =
        sized <| fun n ->
            gen {   let! k = choose (0,n)
                    return! vectorOf k gn }

    /// Generates a non-empty list of random length. The maximum length 
    /// depends on the size parameter.
    let nonEmptyListOf gn =
        sized <| fun n ->
            gen {   let! k = choose (1,max 1 n)
                    return! vectorOf k gn }

    ///Always generate v.          
    let constant v = gen { return v }

    ///Promote the given function f to a function generator.
    let promote f = Gen (fun n r -> fun a -> let (Gen m) = f a in m n r)

    ///Basic co-arbitrary generator transformer, which is dependent on an int.
    let variant v (Gen m) =
        let rec rands r0 = seq { let r1,r2 = split r0 in yield r1; yield! (rands r2) }
        Gen (fun n r -> m n (Seq.nth (v+1) (rands r)))

    let private Arbitrary = ref <| TypeClass<Arbitrary<obj>>.New()

    ///Returns a Gen<'a>
    let arbitrary<'a> = (!Arbitrary).InstanceFor<'a,Arbitrary<'a>>().Arbitrary // |> (fun arb -> arb.Arbitrary)

    ///Returns a generator transformer for the given value, aka a coarbitrary function.
    let coarbitrary<'a,'b> (a:'a) : (Gen<'b> -> Gen<'b>) = 
        (!Arbitrary).InstanceFor<'a,Arbitrary<'a>>().CoArbitrary a

    ///Returns the immediate shrinks for the given value.
    let shrink<'a> (a:'a) = 
        (!Arbitrary).InstanceFor<'a,Arbitrary<'a>>().Shrink a

    let internal getGenerator t = (!Arbitrary).GetInstance t |> unbox<IArbitrary> |> (fun arb -> arb.ArbitraryObj)

    let internal getShrink t = (!Arbitrary).GetInstance t |> unbox<IArbitrary> |> (fun arb -> arb.ShrinkObj)

    ///Register the generators that are static members of the given type.
    let registerByType t = 
        let newTypeClass = (!Arbitrary).Discover(onlyPublic=true,instancesType=t)
        let result = (!Arbitrary).Compare newTypeClass
        Arbitrary := (!Arbitrary).Merge newTypeClass
        result

    ///Register the generators that are static members of the type argument.
    let register<'t>() = 
        //initArbitraryTypeClass.Value
        registerByType typeof<'t>


    //---obsoleted functions-----

    [<Obsolete("This function has been renamed to map, and will be removed in the following version of FsCheck.")>]
    let fmapGen = map

    [<Obsolete("This function has been renamed to map, and will be removed in the following version of FsCheck.")>]
    let liftGen = map
    [<Obsolete("This function has been renamed to map2, and will be removed in the following version of FsCheck.")>]
    let liftGen2 = map2
    [<Obsolete("This function has been renamed to map3, and will be removed in the following version of FsCheck.")>]
    let liftGen3 = map3
    [<Obsolete("This function has been renamed to map4, and will be removed in the following version of FsCheck.")>]
    let liftGen4 = map4
    [<Obsolete("This function has been renamed to map5, and will be removed in the following version of FsCheck.")>]
    let liftGen5 = map5
    [<Obsolete("This function has been renamed to map6, and will be removed in the following version of FsCheck.")>]
    let liftGen6 = map6

    [<Obsolete("This function has been renamed to registerByType, and will be removed in the following version of FsCheck.")>]
    let registerGeneratorsByType = registerByType
   
    [<Obsolete("This function has been renamed to register, and will be removed in the following version of FsCheck.")>]
    let registerGenerators<'t> = register<'t>


