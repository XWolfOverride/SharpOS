/**
 *  (C) 2006-2007 Mircea-Cristian Racasan <darx_kies@gmx.net>
 * 
 *  Licensed under the terms of the GNU GPL License version 2.
 * 
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using SharpOS.AOT.IR;
using SharpOS.AOT.IR.Instructions;
using SharpOS.AOT.IR.Operands;
using SharpOS.AOT.IR.Operators;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;


namespace SharpOS.AOT.IR
{
    
    public partial class Engine : IEnumerable<Method>
    {
        public Engine()
		{
		}

        private IAssembly asm = null;

        public IAssembly Assembly
        {
            get { return asm; }
        }
	
        public void Run(IAssembly asm, string assembly, string target)
        {
            this.asm = asm;

            AssemblyDefinition library = AssemblyFactory.GetAssembly(assembly);
         
            foreach (TypeDefinition type in library.MainModule.Types)
            {
                Console.WriteLine(type.Name);

                if (type.Name.Equals("<Module>") == true)
                {
                    continue;
                }

                Console.WriteLine(type.FullName);

                foreach (MethodDefinition entry in type.Methods)
                {
                    Method method = new Method(this, entry);

                    method.Process();
                }
            }

            asm.Encode(this, target);

            return;
        }

        private List<Method> methods = new List<Method>();

        IEnumerator<Method> IEnumerable<Method>.GetEnumerator()
        {
            foreach (Method method in this.methods)
            {
                yield return method;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Method>)this).GetEnumerator();
        }
    }

    public class Method : IEnumerable<Block>
    {
        public Method(Engine engine, MethodDefinition methodDefinition)
        {
            this.engine = engine;
            this.methodDefinition = methodDefinition;
        }

        private Engine engine = null;
        private MethodDefinition methodDefinition = null;

        public MethodDefinition MethodDefinition
        {
            get { return this.methodDefinition; }
        }

        public string Dump()
        {
            return Dump(blocks);
        }

        public string Dump(List<Block> list)
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("===============================\n");
            stringBuilder.Append("->" + this.methodDefinition.Name + "\n");
            stringBuilder.Append("===============================\n");

            foreach (Block block in list)
            {
                block.Dump(string.Empty, stringBuilder);
            }

            return stringBuilder.ToString();
        }

        public bool IsBranch(Mono.Cecil.Cil.Instruction instruction, bool all)
        {
            if (all == true && instruction.OpCode == OpCodes.Ret)
            {
                return true;
            }

            if (instruction.OpCode == OpCodes.Br
                || instruction.OpCode == OpCodes.Br_S
                || instruction.OpCode == OpCodes.Brfalse
                || instruction.OpCode == OpCodes.Brfalse_S
                || instruction.OpCode == OpCodes.Brtrue
                || instruction.OpCode == OpCodes.Brtrue_S
                || instruction.OpCode == OpCodes.Beq
                || instruction.OpCode == OpCodes.Beq_S
                || instruction.OpCode == OpCodes.Bge
                || instruction.OpCode == OpCodes.Bge_S
                || instruction.OpCode == OpCodes.Bge_Un
                || instruction.OpCode == OpCodes.Bge_Un_S
                || instruction.OpCode == OpCodes.Bgt
                || instruction.OpCode == OpCodes.Bgt_S
                || instruction.OpCode == OpCodes.Bgt_Un
                || instruction.OpCode == OpCodes.Bgt_Un_S
                || instruction.OpCode == OpCodes.Ble
                || instruction.OpCode == OpCodes.Ble_S
                || instruction.OpCode == OpCodes.Ble_Un
                || instruction.OpCode == OpCodes.Ble_Un_S
                || instruction.OpCode == OpCodes.Blt
                || instruction.OpCode == OpCodes.Blt_S
                || instruction.OpCode == OpCodes.Blt_Un
                || instruction.OpCode == OpCodes.Blt_Un_S
                || instruction.OpCode == OpCodes.Bne_Un
                || instruction.OpCode == OpCodes.Bne_Un_S
                || instruction.OpCode == OpCodes.Switch
                || instruction.OpCode == OpCodes.Leave
                || instruction.OpCode == OpCodes.Leave_S
                || instruction.OpCode == OpCodes.Endfinally
                || instruction.OpCode == OpCodes.Throw
                || instruction.OpCode == OpCodes.Rethrow)
            {
                return true;
            }

            return false;
        }

        private void BuildBlocks()
        {
            blocks = new List<Block>();

            Block currentBlock = new Block(this);
            blocks.Add(currentBlock);

            // 1st Step: Split the code in blocks that branch at the end
            for (int i = 0; i < this.methodDefinition.Body.Instructions.Count; i++)
            {
                Mono.Cecil.Cil.Instruction instruction = this.methodDefinition.Body.Instructions[i];

                currentBlock.CIL.Add(instruction);

                if (i < this.methodDefinition.Body.Instructions.Count - 1 && IsBranch(instruction, true) == true)
                {
                    currentBlock = new Block(this);
                    blocks.Add(currentBlock);
                }
            }

            // 2nd Step: Split the blocks if their code is referenced by other branches
            bool found;

            do
            {
                found = false;

                foreach (Block source in blocks)
                {
                    if (this.IsBranch(source.CIL[source.CIL.Count - 1], false) == true
                        && (source.CIL[source.CIL.Count - 1].Operand is Mono.Cecil.Cil.Instruction
                        || source.CIL[source.CIL.Count - 1].Operand is Mono.Cecil.Cil.Instruction[]))
                    {
                        List<Mono.Cecil.Cil.Instruction> jumps = new List<Mono.Cecil.Cil.Instruction>();

                        if (source.CIL[source.CIL.Count - 1].Operand is Mono.Cecil.Cil.Instruction)
                        {
                            jumps.Add(source.CIL[source.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction);
                        }
                        else
                        {
                            jumps = new List<Mono.Cecil.Cil.Instruction>(source.CIL[source.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction[]);
                        }

                        foreach (Mono.Cecil.Cil.Instruction jump in jumps)
                        {
                            if (jump == source.CIL[source.CIL.Count - 1])
                            {
                                continue;
                            }

                            for (int destinationIndex = 0; destinationIndex < blocks.Count; destinationIndex++)
                            {
                                Block destination = blocks[destinationIndex];
                                Block newBlock = new Block(this);

                                for (int i = 0; i < destination.CIL.Count; i++)
                                {
                                    Mono.Cecil.Cil.Instruction instruction = destination.CIL[i];

                                    if (instruction == jump)
                                    {
                                        if (i == 0)
                                        {
                                            break;
                                        }

                                        found = true;
                                    }

                                    if (found == true)
                                    {
                                        newBlock.CIL.Add(destination.CIL[i]);
                                    }
                                }

                                if (found == true)
                                {
                                    for (int i = 0; i < newBlock.CIL.Count; i++)
                                    {
                                        destination.CIL.Remove(newBlock.CIL[i]);
                                    }

                                    blocks.Insert(destinationIndex + 1, newBlock);

                                    break;
                                }
                            }

                            if (found == true)
                            {
                                break;
                            }
                        }
                    }

                    if (found == true)
                    {
                        break;
                    }
                }
            }
            while (found == true);

            // 3rd step: split the try blocks in case they got mixed up with some other code
            do
            {
                found = false;

                foreach (ExceptionHandler exceptionHandler in this.methodDefinition.Body.ExceptionHandlers)
                {
                    for (int i = 0; i < this.blocks.Count; i++)
                    {
                        Block block = this.blocks[i];

                        if (exceptionHandler.TryStart.Offset > block.StartOffset
                            && exceptionHandler.TryStart.Offset <= block.EndOffset)
                        {
                            Block newBlock = new Block(this);

                            for (int j = 0; j < block.CIL.Count; j++)
                            {
                                Mono.Cecil.Cil.Instruction instruction = block.CIL[j];

                                if (instruction == exceptionHandler.TryStart)
                                {
                                    found = true;
                                }

                                if (found == true)
                                {
                                    newBlock.CIL.Add(block.CIL[j]);
                                }
                            }

                            for (int j = 0; j < newBlock.CIL.Count; j++)
                            {
                                block.CIL.Remove(newBlock.CIL[j]);
                            }

                            blocks.Insert(i + 1, newBlock);

                            break;
                        }

                        if (block.StartOffset > exceptionHandler.TryStart.Offset)
                        {
                            break;
                        }
                    }

                    if (found == true)
                    {
                        break;
                    }
                }

            }
            while (found == true);

            return;
        }

        private void FillOuts(Block destination, Mono.Cecil.Cil.Instruction[] instructions)
        {
            foreach (Mono.Cecil.Cil.Instruction instruction in instructions)
            {
                bool found = false;

                foreach (Block block in blocks)
                {
                    if (block.CIL[0] == instruction)
                    {
                        found = true;

                        destination.Outs.Add(block);

                        break;
                    }
                }

                if (found == false)
                {
                    throw new Exception("Could not find the block for the instruction at offset '" + instruction.Offset + "'.");
                }
            }

            return;
        }

        private void ClassifyAndLinkBlocks()
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                Block block = blocks[i];

                if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Ret)
                {
                    block.Type = Block.BlockType.Return;
                }
                else if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Switch)
                {
                    block.Type = Block.BlockType.NWay;

                    this.FillOuts(block, block.CIL[block.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction[]);
                }
                else if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Br
                    || block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Br_S)
                {
                    block.Type = Block.BlockType.OneWay;

                    this.FillOuts(block, new Mono.Cecil.Cil.Instruction[] { block.CIL[block.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction });
                }
                else if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Endfinally)
                {
                    block.Type = Block.BlockType.OneWay; // Block.BlockType.Finally;
                }
                else if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Throw
                   || block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Rethrow)
                {
                    block.Type = Block.BlockType.Throw;
                }
                else if (block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Leave
                    || block.CIL[block.CIL.Count - 1].OpCode == OpCodes.Leave_S)
                {
                    Mono.Cecil.Cil.Instruction lastInstruction = block.CIL[block.CIL.Count - 1];

                    this.FillOuts(block, new Mono.Cecil.Cil.Instruction[] { block.CIL[block.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction });
                    
                    bool found = false;

                    foreach (ExceptionHandler exceptionHandler in block.Method.MethodDefinition.Body.ExceptionHandlers)
                    {
                        if (exceptionHandler.TryEnd.Previous == lastInstruction)
                        {
                            found = true;
                            block.Type = Block.BlockType.OneWay; // Block.BlockType.Try;

                            break;
                        }
                        else if (exceptionHandler.HandlerEnd.Previous == lastInstruction)
                        {
                            found = true;
                            block.Type = Block.BlockType.OneWay; // Block.BlockType.Catch;

                            break;
                        }
                    }

                    if (found == false)
                    {
                        throw new Exception("Malformated Try/Catch block in '" + block.Method.MethodDefinition.Name + "'.");
                    }
                }
                else if (this.IsBranch(block.CIL[block.CIL.Count - 1], false) == true)
                {
                    block.Type = Block.BlockType.TwoWay;

                    this.FillOuts(block, new Mono.Cecil.Cil.Instruction[] { block.CIL[block.CIL.Count - 1].Operand as Mono.Cecil.Cil.Instruction });
                    
                    block.Outs.Add(blocks[i + 1]);
                }
                else
                {
                    block.Type = Block.BlockType.Fall;
                    block.Outs.Add(blocks[i + 1]);
                }
            }

            // Fill The Ins
            for (int i = 0; i < blocks.Count; i++)
            {
                for (int j = 0; j < blocks.Count; j++)
                {
                    if (blocks[j].Outs.Contains(blocks[i]) == true)
                    {
                        blocks[i].Ins.Add(blocks[j]);
                    }
                }
            }

            return;
        }

        public void BlocksOptimization()
        {
            bool changed;
            do
            {
                changed = false;
                for (int i = 1; i < this.blocks.Count; i++)
                {
                    if (this.blocks[i].Ins.Count == 1
                        && this.blocks[i - 1].Outs.Count == 1
                        && this.blocks[i].Ins[0] == this.blocks[i - 1]
                        && this.blocks[i - 1].Outs[0] == this.blocks[i])
                    {
                        this.blocks[i - 1].Merge(this.blocks[i]);

                        this.blocks.Remove(this.blocks[i]);

                        changed = true;
                        break;
                    }
                }
            }
            while (changed == true);

            for (int i = 0; i < this.blocks.Count; i++)
            {
                this.blocks[i].Index = i;
            }

            return;
        }

        private void ConvertFromCIL()
        {
            foreach (Block block in blocks)
            {
                block.ConvertFromCIL(false);
            }

            List<Block> removeFirstInstruction = new List<Block>();

            foreach (Block block in blocks)
            {
                if (block.Stack > 0 && removeFirstInstruction.Contains(block) == false && block.CIL[block.CIL.Count - 1].OpCode != OpCodes.Ret)
                {
                    if (block.Outs.Count != 1)
                    {
                        throw new Exception("Could not convert '" + block.Method.MethodDefinition.Name + "' from CIL.");
                    }

                    Block childBlock = block.Outs[0];

                    while (childBlock.Type == Block.BlockType.OneWay && childBlock.CIL.Count == 1)
                    {
                        childBlock = childBlock.Outs[0];
                    }

                    if (removeFirstInstruction.Contains(childBlock) == false)
                    {
                        removeFirstInstruction.Add(childBlock);
                    }

                    Mono.Cecil.Cil.Instruction instruction = childBlock.CIL[0];

                    if (block.CIL[block.CIL.Count - 1].OpCode.FlowControl == FlowControl.Branch)
                    {
                        block.CIL.Insert(block.CIL.Count - 1, instruction);
                    }
                    else
                    {
                        block.CIL.Add(instruction);
                    }
                }
            }

            if (removeFirstInstruction.Count > 0)
            {
                foreach (Block block in removeFirstInstruction)
                {
                    block.CIL.RemoveAt(0);
                }

                foreach (Block block in blocks)
                {
                    block.ConvertFromCIL(true);
                }
            }

            return;
        }

        private List<Block> Preorder()
        {
            List<Block> list = new List<Block>();
            List<Block> visited = new List<Block>();

            Preorder(visited, list, this.blocks[0]);

            return list;
        }

        private void Preorder(List<Block> visited, List<Block> list, Block current)
        {
            if (visited.Contains(current) == false)
            {
                visited.Add(current);

                list.Add(current);

                for (int i = 0; i < current.Outs.Count; i++)
                {
                    Preorder(visited, list, current.Outs[i]);
                }
            }

            return;
        }

        private List<Block> Postorder()
        {
            List<Block> list = new List<Block>();
            List<Block> visited = new List<Block>();

            Postorder(visited, list, this.blocks[0]);

            return list;
        }

        private void Postorder(List<Block> visited, List<Block> list, Block current)
        {
            if (visited.Contains(current) == false)
            {
                visited.Add(current);

                for (int i = 0; i < current.Outs.Count; i++)
                {
                    Postorder(visited, list, current.Outs[i]);
                }

                list.Add(current);
            }

            return;
        }

        private List<Block> ReversePostorder()
        {
            List<Block> list = new List<Block>();
            List<Block> visited = new List<Block>();
            List<Block> active = new List<Block>();

            visited.Add(this.blocks[0]);
            list.Add(this.blocks[0]);

            ReversePostorder(visited, active, list, this.blocks[0]);

            return list;
        }

        private void ReversePostorder(List<Block> visited, List<Block> active, List<Block> list, Block current)
        {
            if (active.Contains(current) == true)
            {
                return;
            }

            active.Add(current);

            for (int i = 0; i < current.Outs.Count; i++)
            {
                if (visited.Contains(current.Outs[i]) == false)
                {
                    visited.Add(current.Outs[i]);
                    list.Add(current.Outs[i]);
                }
            }

            for (int i = 0; i < current.Outs.Count; i++)
            {
                ReversePostorder(visited, active, list, current.Outs[i]);
            }
            
            active.Remove(current);

            return;
        }

        private void Dominators()
        {
            for (int i = 0; i < this.blocks.Count; i++)
            {
                foreach (Block block in blocks)
                {
                    this.blocks[i].Dominators.Add(block);
                }
            }

            List<Block> list = Preorder();

            bool changed = true;

            while (changed == true)
            {
                changed = false;

                for (int i = 0; i < list.Count; i++)
                {
                    List<Block> predecessorDoms = new List<Block>();
                    List<Block> doms = new List<Block>();

                    Block block = list[list.Count - 1 - i];

                    // Add the dominator blocks of the predecessors
                    foreach (Block predecessor in block.Ins)
                    {
                        foreach (Block dom in predecessor.Dominators)
                        {
                            if (predecessorDoms.Contains(dom) == false)
                            {
                                predecessorDoms.Add(dom);
                            }
                        }
                    }

                    // For each block in the predecessors' dominators build the intersection
                    foreach (Block predecessorDom in predecessorDoms)
                    {
                        bool include = true;

                        foreach (Block predecessor in block.Ins)
                        {
                            if (predecessor.Dominators.Contains(predecessorDom) == false)
                            {
                                include = false;
                                break;
                            }
                        }

                        if (include == true)
                        {
                            doms.Add(predecessorDom);
                        }
                    }

                    // Add the block itself to the dominators
                    doms.Add(block);

                    // Set the new dominators if there are any differences
                    if (block.Dominators.Count != doms.Count)
                    {
                        block.Dominators = doms;
                        changed = true;
                    }
                    else
                    {
                        foreach (Block dom in doms)
                        {
                            if (block.Dominators.Contains(dom) == false)
                            {
                                block.Dominators = doms;
                                changed = true;
                                break;
                            }
                        }
                    }
                }
            }

            // Compute the Immediate Dominator of each Block
            foreach (Block block in blocks)
            {
                foreach (Block immediateDominator in block.Dominators)
                {
                    // An Immediate Dominator can't be the block itself
                    if (immediateDominator == block)
                    {
                        continue;
                    }

                    bool found = false;

                    foreach (Block dominator in block.Dominators)
                    {
                        if (dominator == immediateDominator || dominator == block)
                        {
                            continue;
                        }

                        // An Immediate Dominator can't dominate another Dominator only the block itself
                        if (dominator.Dominators.Contains(immediateDominator) == true)
                        {
                            found = true;
                            break;
                        }
                    }

                    // We found the Immediate Dominator that does not dominate any other dominator but the block itself
                    if (found == false)
                    {
                        block.ImmediateDominator = immediateDominator;
                        break;
                    }
                }
            }

            // Build the Dominator Tree. The Parent of a Node is the Immediate Dominator of that block.
            foreach (Block parent in blocks)
            {
                foreach (Block possibleChild in blocks)
                {
                    if (parent == possibleChild.ImmediateDominator)
                    {
                        parent.ImmediateDominatorOf.Add(possibleChild);
                    }
                }
            }

            // Compute the Dominance Frontier
            foreach (Block block in blocks)
            {
                if (block.Ins.Count > 1)
                {
                    foreach (Block predecessor in block.Ins)
                    {
                        Block runner = predecessor;

                        while (runner != block.ImmediateDominator)
                        //&& runner != block) // In case we got back throu a Backwards link
                        {
                            runner.DominanceFrontiers.Add(block);
                            runner = runner.ImmediateDominator;
                        }
                    }
                }
            }

            Console.WriteLine("=======================================");
            Console.WriteLine("Dominator");
            Console.WriteLine("=======================================");

            foreach (Block block in this.blocks)
            {
                StringBuilder stringBuilder = new StringBuilder();

                if (block.ImmediateDominator == null)
                {
                    stringBuilder.Append("<>");
                }
                else
                {
                    stringBuilder.Append("<" + block.ImmediateDominator.Index + ">");
                }

                stringBuilder.Append(" " + block.Index + " -> [");

                foreach (Block dominator in block.Dominators)
                {
                    if (dominator != block.Dominators[0])
                    {
                        stringBuilder.Append(", ");
                    }

                    stringBuilder.Append(dominator.Index);
                }

                stringBuilder.Append("]");

                Console.WriteLine(stringBuilder.ToString());
            }

            Console.WriteLine("=======================================");
            Console.WriteLine("Dominator Tree");
            Console.WriteLine("=======================================");

            foreach (Block parent in blocks)
            {
                if (parent.ImmediateDominatorOf.Count > 0)
                {
                    StringBuilder stringBuilder = new StringBuilder();

                    stringBuilder.Append(parent.Index + " -> [");

                    foreach (Block child in parent.ImmediateDominatorOf)
                    {
                        if (child != parent.ImmediateDominatorOf[0])
                        {
                            stringBuilder.Append(", ");
                        }

                        stringBuilder.Append(child.Index);
                    }

                    stringBuilder.Append("]");

                    Console.WriteLine(stringBuilder.ToString());
                }
            }


            Console.WriteLine("=======================================");
            Console.WriteLine("Dominance Frontiers");
            Console.WriteLine("=======================================");

            foreach (Block parent in blocks)
            {
                if (parent.DominanceFrontiers.Count > 0)
                {
                    StringBuilder stringBuilder = new StringBuilder();

                    stringBuilder.Append(parent.Index + " -> [");

                    foreach (Block child in parent.DominanceFrontiers)
                    {
                        if (child != parent.DominanceFrontiers[0])
                        {
                            stringBuilder.Append(", ");
                        }

                        stringBuilder.Append(child.Index);
                    }

                    stringBuilder.Append("]");

                    Console.WriteLine(stringBuilder.ToString());
                }
            }

            return;
        }

        private void AddVariable(Dictionary<SharpOS.AOT.IR.Operands.Identifier, List<Block>> identifierList, Identifier identifier, Block block)
        {
            /*if (identifier is SharpOS.AOT.IR.Operands.Register == true)
            {
                return;
            }*/

            foreach (Identifier key in identifierList.Keys)
            {
                if (key.ToString().Equals(identifier.ToString()) == true)
                {
                    if (identifierList[key].Contains(block) == false)
                    {
                        identifierList[key].Add(block);
                    }

                    return; ;
                }
            }

            List<Block> list = new List<Block>();
            list.Add(block);
            identifierList[identifier.Clone() as Identifier] = list;

            return;
        }

        private void TransformationToSSA()
        {
            Dictionary<SharpOS.AOT.IR.Operands.Identifier, List<Block>> identifierList = new Dictionary<SharpOS.AOT.IR.Operands.Identifier, List<Block>>();

            // Find out in which blocks every variable gets defined
            foreach (Block block in blocks)
            {
                foreach (SharpOS.AOT.IR.Instructions.Instruction instruction in block)
                {
                    if (instruction is Assign)
                    {
                        Assign assign = instruction as Assign;

                        this.AddVariable(identifierList, assign.Asignee, block);
                    }
                }
            }

            // Insert PHI
            foreach (Identifier identifier in identifierList.Keys)
            {
                List<Block> list = identifierList[identifier];
                List<Block> everProcessed = new List<Block>();

                foreach (Block block in list)
                {
                    everProcessed.Add(block);
                }

                do
                {
                    Block block = list[0];
                    list.RemoveAt(0);

                    foreach (Block dominanceFrontier in block.DominanceFrontiers)
                    {
                        bool found = false;

                        // Is the PHI for the current variable already in the block?
                        foreach (SharpOS.AOT.IR.Instructions.Instruction instruction in dominanceFrontier)
                        {
                            if (instruction is PHI == false)
                            {
                                break;
                            }

                            Assign phi = instruction as Assign;
                            string id = phi.Asignee.Value.ToString();

                            if (id.Equals(identifier.Value.ToString()) == true)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (found == false)
                        {
                            Operand[] operands = new Operand[dominanceFrontier.Ins.Count];

                            for (int i = 0; i < operands.Length; i++)
                            {
                                operands[i] = identifier.Clone();
                            }

                            PHI phi = new PHI(identifier.Clone() as Identifier, new Operands.Miscellaneous(new Operators.Miscellaneous(Operator.MiscellaneousType.InternalList), operands));
                            dominanceFrontier.InsertInstruction(0, phi);

                            if (everProcessed.Contains(dominanceFrontier) == false)
                            {
                                everProcessed.Add(dominanceFrontier);
                                list.Add(dominanceFrontier);
                            }
                        }
                    }
                }
                while (list.Count > 0);
            }

            // Rename the Variables
            foreach (Block block in blocks)
            {
                Dictionary<string, int> count = new Dictionary<string, int>();
                Dictionary<string, Stack<int>> stack = new Dictionary<string, Stack<int>>();

                foreach (Identifier identifier in identifierList.Keys)
                {
                    count[identifier.Value.ToString()] = 0;
                    stack[identifier.Value.ToString()] = new Stack<int>();
                    stack[identifier.Value.ToString()].Push(0);
                }

                this.SSARename(this.blocks[0], count, stack);
            }

            return;
        }

        private int GetSSAStackValue(Dictionary<string, Stack<int>> stack, string name)
        {
            if (stack.ContainsKey(name) == false)
            {
                return 0;
            }

            return stack[name].Peek();
        }

        private void SSARename(Block block, Dictionary<string, int> count, Dictionary<string, Stack<int>> stack)
        {
            foreach (SharpOS.AOT.IR.Instructions.Instruction instruction in block)
            {
                // Update the Operands of the instruction (A = B -> A = B5)
                if (instruction is PHI == false && instruction.Value != null)
                {
                    if (instruction.Value.Operands != null)
                    {
                        foreach (Operand operand in instruction.Value.Operands)
                        {
                            if (operand is Identifier == true)
                            {
                                Identifier identifier = operand as Identifier;

                                identifier.Version = GetSSAStackValue(stack, identifier.Value.ToString());
                            }
                        }
                    }
                    else if (instruction.Value is Identifier == true)
                    {
                        Identifier identifier = instruction.Value as Identifier;

                        identifier.Version = GetSSAStackValue(stack, identifier.Value.ToString());
                    }
                }

                // Update the Definition of a variaable (e.g. A = ... -> A3 = ...)
                if (instruction is Assign == true)
                //|| instruction is PHI == true)
                {
                    string id = (instruction as Assign).Asignee.Value.ToString();

                    count[id]++;
                    stack[id].Push(count[id]);

                    (instruction as Assign).Asignee.Version = count[id];
                }
            }

            // Now update the PHI of the successors
            foreach (Block successor in block.Outs)
            {
                int j = 0;
                bool found = false;

                // Find the position of the link to the successor in the successor itself
                foreach (Block predecessor in successor.Ins)
                {
                    if (predecessor == block)
                    {
                        found = true;
                        break;
                    }

                    j++;
                }

                if (found == false)
                {
                    throw new Exception("Could not find the successor position.");
                }

                // The update the PHI Values
                foreach (Instructions.Instruction instruction in successor)
                {
                    if (instruction is PHI == false)
                    {
                        break;
                    }

                    PHI phi = instruction as PHI;
                    phi.Value.Operands[j].Version = stack[(phi as Assign).Asignee.Value.ToString()].Peek();
                }
            }

            // Descend in the Dominator Tree and do the "SSA Thing"
            foreach (Block child in block.ImmediateDominatorOf)
            {
                this.SSARename(child, count, stack);
            }

            // Pull from the stack the variable versions of the current block 
            foreach (SharpOS.AOT.IR.Instructions.Instruction instruction in block)
            {
                if (instruction is Assign == true)
                {
                    Assign assign = instruction as Assign;

                    stack[assign.Asignee.Value.ToString()].Pop();
                }
            }

            return;
        }

        Dictionary<string, List<Instructions.Instruction>> defuse;

        // The first entry in every list of each variable is the definition instruction, the others are the instructions that use the variable.
        private void GetListOfDefUse()
        {
            defuse = new Dictionary<string, List<Instructions.Instruction>>();

            foreach (Block block in this.blocks)
            {
                foreach (Instructions.Instruction instruction in block)
                {
                    if (instruction.Value != null)
                    {
                        foreach (Operand operand in instruction.Value.Operands)
                        {
                            if (operand is Identifier == true)
                            {
                                string id = operand.ToString();

                                if (defuse.ContainsKey(id) == false)
                                {
                                    defuse[id] = new List<SharpOS.AOT.IR.Instructions.Instruction>();
                                    defuse[id].Add(null);

                                    if (operand.Version == 0)
                                    {
                                        defuse[id][0] = new Instructions.System(new SharpOS.AOT.IR.Operands.Miscellaneous(new Operators.Miscellaneous(Operator.MiscellaneousType.Argument)));
                                        defuse[id][0].Block = this.blocks[0];
                                    }
                                }
                                
                                if (defuse[id].Contains(instruction) == false)
                                {
                                    defuse[id].Add(instruction);
                                }
                            }
                        }
                    }

                    if (instruction is Assign == true)
                    {
                        string id = (instruction as Assign).Asignee.ToString();

                        if (defuse.ContainsKey(id) == false)
                        {
                            defuse[id] = new List<SharpOS.AOT.IR.Instructions.Instruction>();
                            defuse[id].Add(instruction);
                        }
                        else
                        {
                            if (defuse[id][0] != null)
                            {
                                throw new Exception("SSA variable '" + id + "' in '" + this.MethodFullName + "' defined a second time.");
                            }

                            defuse[id][0] = instruction;
                        }
                    }
                }
            }

            Console.WriteLine("=======================================");
            Console.WriteLine("Def-Use");
            Console.WriteLine("=======================================");

            foreach (string key in defuse.Keys)
            {
                List<Instructions.Instruction> list = defuse[key];

                if (list[0] == null)
                {
                    throw new Exception("Def statement for '" + key + "' in '" + this.MethodFullName + "' not found.");
                }

                Console.WriteLine(list[0].Block.Index + " : " + list[0].ToString());

                for (int i = 1; i < list.Count; i++)
                {
                    Console.WriteLine("\t" + list[i].Block.Index + " : " + list[i]);
                }
            }

            return;
        }

        private void DeadCodeElimination()
        {
            string[] values = new string[this.defuse.Keys.Count];
            this.defuse.Keys.CopyTo(values, 0);
            List<string> keys = new List<string>(values);

            Console.WriteLine("=======================================");
            Console.WriteLine("Dead Code Elimination");
            Console.WriteLine("=======================================");

            while (keys.Count > 0)
            {
                string key = keys[0];
                keys.RemoveAt(0);

                List<Instructions.Instruction> list = this.defuse[key];

                // This variable is only defined but not used
                if (list.Count == 1)
                {
                    // A = B + C;
                    Instructions.Instruction definition = list[0];

                    Console.WriteLine(definition.Block.Index + " : " + definition.ToString());

                    // Remove the instruction from the block that it is containing it
                    definition.Block.RemoveInstruction(definition);

                    // Remove the variable from the defuse list
                    defuse.Remove(key);

                    if (definition.Value != null
                        && definition is Instructions.System == false)
                    {
                        // B & C used in "A = B + C"
                        foreach (Operand operand in definition.Value.Operands)
                        {
                            if (operand is Identifier == true)
                            {
                                string id = operand.ToString();

                                // Remove "A = B + C" from B & C
                                this.defuse[id].Remove(definition);

                                // Add to the queue B & C to check them it they are used anywhere else
                                if (keys.Contains(id) == false)
                                {
                                    keys.Add(id);
                                }
                            }
                        }
                    }
                }
            }

            return;
        }

        private void SimpleConstantPropagation()
        {
            string[] values = new string[this.defuse.Keys.Count];
            this.defuse.Keys.CopyTo(values, 0);
            List<string> keys = new List<string>(values);

            Console.WriteLine("=======================================");
            Console.WriteLine("Simple Constant Propagation");
            Console.WriteLine("=======================================");

            while (keys.Count > 0)
            {
                string key = keys[0];
                keys.RemoveAt(0);

                List<Instructions.Instruction> list = this.defuse[key];

                Instructions.Instruction definition = list[0];

                Console.WriteLine(definition.Block.Index + " : " + definition.ToString());

                // v2 = PHI(v1, v1)
                if (definition is PHI == true)
                {
                    Operand sample = definition.Value.Operands[0];

                    bool equal = true;

                    for (int i = 1; i < definition.Value.Operands.Length; i++)
                    {
                        if (sample.ToString().Equals(definition.Value.Operands[i].ToString()) == false)
                        {
                            equal = false;
                            break;
                        }
                    }

                    if (equal == true)
                    {
                        Assign assign = new Assign((definition as Assign).Asignee, sample);

                        // Replace the PHI with a normal assignment
                        definition.Block.RemoveInstruction(definition);
                        definition.Block.InsertInstruction(0, assign);

                        defuse[key][0] = assign;
                    }
                }
                // A = 100
                else if (definition is Assign == true && (definition as Assign).Value is Constant == true)
                {

                    // Remove the instruction from the block that it is containing it
                    definition.Block.RemoveInstruction(definition);

                    // Remove the variable from the defuse list
                    defuse.Remove(key);

                    // "X = A" becomes "X = 100"
                    for (int i = 1; i < list.Count; i++)
                    {
                        Instructions.Instruction used = list[i];
                        
                        if (used.Value != null)
                        {
                            for (int j = 0; j < used.Value.Operands.Length; j++)
                            {
                                Operand operand = used.Value.Operands[j];

                                // Replace A with 100
                                if (operand is Identifier == true
                                    && operand.ToString().Equals(key) == true)
                                {
                                    if (used.Value is Identifier == true)
                                    {
                                        used.Value = definition.Value;
                                    }
                                    else
                                    {
                                        used.Value.Operands[j] = definition.Value;
                                    }
                                }
                            }

                            Console.WriteLine("\t" + definition.Block.Index + " : " + used.ToString());
                        }

                        if (used is Assign == true)
                        {
                            string id = (used as Assign).Asignee.ToString();

                            // Add to the queue 
                            if (keys.Contains(id) == false)
                            {
                                keys.Add(id);
                            }
                        }
                    }
                }
            }

            return;
        }

        private void CopyPropagation()
        {
            string[] values = new string[this.defuse.Keys.Count];
            this.defuse.Keys.CopyTo(values, 0);
            List<string> keys = new List<string>(values);

            Console.WriteLine("=======================================");
            Console.WriteLine("Copy Propagation");
            Console.WriteLine("=======================================");

            while (keys.Count > 0)
            {
                string key = keys[0];
                keys.RemoveAt(0);

                List<Instructions.Instruction> list = this.defuse[key];

                Instructions.Instruction definition = list[0];

                // A = B
                if (definition is Assign == true
                    && (definition as Assign).Asignee is Identifier == true
                    && (definition as Assign).Value is Identifier == true)
                {
                    Console.WriteLine(definition.Block.Index + " : " + definition.ToString());

                    // Remove the instruction from the block that it is containing it
                    definition.Block.RemoveInstruction(definition);

                    // Remove the variable from the defuse list
                    defuse.Remove(key);

                    // "X = A" becomes "X = B"
                    for (int i = 1; i < list.Count; i++)
                    {
                        Instructions.Instruction used = list[i];

                        if (used.Value != null)
                        {
                            for (int j = 0; j < used.Value.Operands.Length; j++)
                            {
                                Operand operand = used.Value.Operands[j];

                                // Replace A with B
                                if (operand is Identifier == true
                                    && operand.ToString().Equals(key) == true)
                                {
                                    if (used.Value is Identifier == true)
                                    {
                                        used.Value = definition.Value;
                                    }
                                    else
                                    {
                                        used.Value.Operands[j] = definition.Value;
                                    }
                                }
                            }

                            Console.WriteLine("\t" + definition.Block.Index + " : " + used.ToString());
                        }

                        if (used is Assign == true)
                        {
                            string id = (used as Assign).Asignee.ToString();

                            // Add to the queue 
                            if (keys.Contains(id) == false)
                            {
                                keys.Add(id);
                            }
                        }
                    }
                }
            }

            return;
        }


        public string MethodFullName
        {
            get
            {
                return this.methodDefinition.DeclaringType.FullName + "." + this.methodDefinition.Name;
            }
        }

        // If a block that has many predecessors is linked to a block that has many successors 
        // then an empty edge is inserted. Its used later for the transformation out of SSA.
        public void EdgeSplit()
        {
            foreach (Block block in Preorder())
            {
                if (block.Ins.Count > 1)
                {
                    for (int i = 0; i < block.Ins.Count; i++)
                    {
                        Block predecessor = block.Ins[i];

                        if (predecessor.Outs.Count > 1)
                        {
                            int position = 0;

                            for (; position < predecessor.Outs.Count && predecessor.Outs[position] != block; position++) ;

                            if (position == predecessor.Outs.Count)
                            {
                                throw new Exception("In '" + this.MethodFullName + "' Block " + predecessor.Index + " is not linked to the Block " + block.Index + ".");
                            }

                            Block split = new Block();

                            split.Index = this.blocks[this.blocks.Count - 1].Index + 1;
                            split.Type = Block.BlockType.OneWay;
                            split.InsertInstruction(0, new Jump());
                            split.Ins.Add(predecessor);
                            split.Outs.Add(block);

                            predecessor.Outs[position] = split;
                            block.Ins[i] = split;

                            this.blocks.Add(split);
                        }
                    }
                }
            }
        }

        // Transformation out of SSA
        private void TransformationOutOfSSA()
        {
            foreach (Block block in this.blocks)
            {
                List<Instructions.Instruction> remove = new List<SharpOS.AOT.IR.Instructions.Instruction>();

                foreach (Instructions.Instruction instruction in block)
                {
                    if (instruction is PHI == false)
                    {
                        break;
                    }

                    PHI phi = instruction as PHI;

                    for (int i = 0; i < block.Ins.Count; i++)
                    {
                        Block predecessor = block.Ins[i];

                        Assign assign = new Assign(phi.Asignee.Clone() as Identifier, phi.Value.Operands[i].Clone());

                        int position = predecessor.InstructionsCount;

                        if (predecessor.InstructionsCount > 0
                            && predecessor[predecessor.InstructionsCount - 1] is Jump == true)
                        {
                            position--;
                        }

                        predecessor.InsertInstruction(position, assign);

                        remove.Add(instruction);
                    }
                }

                foreach (Instructions.Instruction instruction in remove)
                {
                    block.RemoveInstruction(instruction);
                }
            }

            return;
        }

        private class LiveRange: IComparable
        {
            public LiveRange(string id, Instructions.Instruction start)
            {
                this.id = id;
                this.start = start;
                this.end = start;
            }

            private string id = string.Empty;

	        public string ID
	        {
		        get { return id;}
		        set { id = value;}
	        }

            private int register = int.MinValue;

            public int Register
            {
                get { return register; }
                set { register = value; }
            }

            private int stack = int.MinValue;

            public int Stack
            {
                get { return stack; }
                set { stack = value; }
            }
	
            private Instructions.Instruction start = null;

            public Instructions.Instruction Start
            {
                get { return start; }
                set { start = value; }
            }

            private Instructions.Instruction end = null;

            public Instructions.Instruction End
            {
                get { return end; }
                set { end = value; }
            }
	
            private List<Operand> identifiers = new List<Operand>();

            public List<Operand> Identifiers
            {
                get { return identifiers; }
                set { identifiers = value; }
            }

            int IComparable.CompareTo(object obj)
            {
                LiveRange liveRange = (LiveRange)obj;

                return this.id.CompareTo(liveRange.id);
            }

            public override string ToString()
            {
                string register = string.Empty;

                if (this.Register != int.MinValue)
                {
                    register = "R" + this.Register;
                }
                else if (this.Stack != int.MinValue)
                {
                    register = "M" + this.Stack;
                }

                return this.ID + " : " + register + " : " + this.Start.Index + " <-> " + this.End.Index;
            }

            public class SortByStart : IComparer<LiveRange>
            {
                int IComparer<LiveRange>.Compare(LiveRange liveRange1, LiveRange liveRange2)
                {
                    if (liveRange1.Start.Index > liveRange2.Start.Index)
                    {
                        return 1;
                    }

                    if (liveRange1.Start.Index < liveRange2.Start.Index)
                    {
                        return -1;
                    }

                    if (liveRange1.End.Index > liveRange2.End.Index)
                    {
                        return 1;
                    }

                    if (liveRange1.End.Index < liveRange2.End.Index)
                    {
                        return -1;
                    }

                    return 0;
                }
            }

            public class SortByEnd : IComparer<LiveRange>
            {
                int IComparer<LiveRange>.Compare(LiveRange liveRange1, LiveRange liveRange2)
                {
                    if (liveRange1.End.Index > liveRange2.End.Index)
                    {
                        return 1;
                    }

                    if (liveRange1.End.Index < liveRange2.End.Index)
                    {
                        return -1;
                    }

                    return 0;
                }
            }
        }

        private void AddLineScaneValue(Dictionary<string, LiveRange> values, Identifier identifier, Instructions.Instruction instruction)
        {
            if (identifier is Argument == true)
            {
                return;
            }

            string id = identifier.ToString();

            if (values.ContainsKey(id) == false)
            {
                LiveRange liveRange = new LiveRange(id, instruction);

                values[id] = liveRange;
            }
            else
            {
                values[id].End = instruction;
            }

            values[id].Identifiers.Add(identifier);
        }

        private List<LiveRange> liveRanges;

        private void LiveRanges()
        {
            int index = 0;
            Dictionary<string, LiveRange> values = new Dictionary<string, LiveRange>();

            foreach (Block block in ReversePostorder())
            {
                foreach (Instructions.Instruction instruction in block)
                {
                    instruction.Index = index++;

                    if (instruction.Value != null)
                    {
                        foreach (Operand operand in instruction.Value.Operands)
                        {
                            if (operand is Identifier == true)
                            {
                                AddLineScaneValue(values, operand as Identifier, instruction);
                            }
                        }
                    }

                    if (instruction is Assign == true)
                    {
                        AddLineScaneValue(values, (instruction as Assign).Asignee, instruction);
                    }
                }
            }

            this.liveRanges = new List<LiveRange>();

            foreach (KeyValuePair<string, LiveRange> entry in values)
            {
                this.liveRanges.Add(entry.Value);
            }

            this.liveRanges.Sort(new LiveRange.SortByStart());

            Console.WriteLine("=======================================");
            Console.WriteLine("Live Ranges");
            Console.WriteLine("=======================================");

            foreach (LiveRange entry in this.liveRanges)
            {
                Console.WriteLine(entry);
            }

            return;
        }

        private void LinearScanRegisterAllocation()
        {
            List<LiveRange> active = new List<LiveRange>();
            List<int> registers = new List<int>();
            int stackPosition = 0;

            for (int i = 0; i < this.engine.Assembly.AvailableRegistersCount; i++)
            {
                registers.Add(i);
            }

            for (int i = 0; i < this.liveRanges.Count; i++)
            {
                ExpireOldIntervals(active, registers, this.liveRanges[i]);

                if (active.Count == this.engine.Assembly.AvailableRegistersCount)
                {
                    SpillAtInterval(active, registers, ref stackPosition, this.liveRanges[i]);
                }
                else
                {
                    int register = registers[0];
                    registers.RemoveAt(0);

                    this.liveRanges[i].Register = register;

                    active.Add(this.liveRanges[i]);
                    active.Sort(new LiveRange.SortByEnd());
                }
            }

            Console.WriteLine("=======================================");
            Console.WriteLine("Linear Scan Register Allocation");
            Console.WriteLine("=======================================");

            foreach (LiveRange entry in this.liveRanges)
            {
                Console.WriteLine(entry);
            }

            return;
        }

        private void ExpireOldIntervals(List<LiveRange> active, List<int> registers, LiveRange liveRange)
        {
            List<LiveRange> remove = new List<LiveRange>();

            foreach (LiveRange value in active)
            {
                if (value.End.Index >= liveRange.Start.Index)
                {
                    break;
                }

                remove.Add(value);
            }

            foreach (LiveRange value in remove)
            {
                registers.Add(value.Register);

                active.Remove(value);
            }
        }

        private void SpillAtInterval(List<LiveRange> active, List<int> registers, ref int stackPosition, LiveRange liveRange)
        {
            LiveRange spill = active[active.Count - 1];

            if (spill.End.Index > liveRange.End.Index)
            {
                liveRange.Register = spill.Register;

                spill.Register = int.MinValue;
                spill.Stack = stackPosition++;

                active.Remove(spill);

                active.Add(liveRange);
                active.Sort(new LiveRange.SortByEnd());
            }
            else
            {
                liveRange.Stack = stackPosition++;
            }
        }

        public void Process()
        {
            if (this.methodDefinition.Body == null)
            {
                return;
            }

            this.BuildBlocks();
            this.ClassifyAndLinkBlocks();
            this.BlocksOptimization();
            this.ConvertFromCIL();
            this.Dominators();
            this.TransformationToSSA();
            this.EdgeSplit();

            Console.WriteLine(this.Dump());

            this.GetListOfDefUse();
            this.DeadCodeElimination();
            this.SimpleConstantPropagation();
            this.CopyPropagation();
            this.TransformationOutOfSSA();

            this.LiveRanges();
            this.LinearScanRegisterAllocation();

            Console.WriteLine(this.Dump(ReversePostorder()));

            return;
        }

        private List<Block> blocks;

        IEnumerator<Block> IEnumerable<Block>.GetEnumerator()
        {
            foreach (Block block in this.blocks)
            {
                yield return block;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<Block>)this).GetEnumerator();
        }
    }

    public interface IAssembly
    {
        bool Encode(Engine engine, string target);
        int AvailableRegistersCount { get; }
    }
}
