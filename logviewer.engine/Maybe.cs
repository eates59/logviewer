﻿// Created by: egr
// Created at: 10.03.2015
// © 2012-2015 Alexander Egorov

using System;
using System.Runtime.CompilerServices;

namespace logviewer.engine
{
    /// <summary>
    /// Maybe monad extensions class
    /// </summary>
    public static class Maybe
    {
        /// <summary>
        /// With monad if input null returns null evaluator result otherwise
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <param name="input">Input instance</param>
        /// <param name="evaluator">Evaluation function</param>
        /// <returns>if input null returns null evaluator result otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult With<TInput, TResult>(this TInput input, Func<TInput, TResult> evaluator) 
            where TInput: class  where TResult: class
        {
            return input == null ? null : evaluator(input);
        }

        /// <summary>
        /// With monad that defines failure result
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <typeparam name="TResult">Result type</typeparam>
        /// <param name="input">Input instance</param>
        /// <param name="evaluator">Evaluation function</param>
        /// <param name="failure">Failure result</param>
        /// <returns>if input null returns null evaluator result otherwise</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult Return<TInput, TResult>(this TInput input, Func<TInput, TResult> evaluator, TResult failure) 
            where TInput: class
        {
            return input == null ? failure : evaluator(input);
        }
        
        /// <summary>
        /// Monad that returns true if input is not null
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <param name="input">Input instance</param>
        /// <returns>if input null returns false otherwise true</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ReturnSuccess<TInput>(this TInput input) 
            where TInput: class
        {
            return input != null;
        }

        /// <summary>
        /// If Monad that returns input if input is not null or evaluator returns true
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <param name="input">Input instance</param>
        /// <param name="evaluator"></param>
        /// <returns>if input null or predicate fails returns null otherwise input instance</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TInput If<TInput>(this TInput input, Predicate<TInput> evaluator) 
            where TInput: class
        {
            if (input == null)
            {
                return null;
            }
            return evaluator(input) ? input : null;
        }

        /// <summary>
        /// Do Monad that runs action if input is not null
        /// </summary>
        /// <typeparam name="TInput">Input type</typeparam>
        /// <param name="input">Input instance</param>
        /// <param name="action">Action to run if input is not null</param>
        /// <returns>if input null returns null otherwise runs action and returns input</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TInput Do<TInput>(this TInput input, Action<TInput> action) 
            where TInput: class
        {
            if (input == null)
            {
                return null;
            }
            action(input);
            return input;
        }
    }
}