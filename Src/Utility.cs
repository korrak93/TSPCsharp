﻿using ILOG.Concert;
using ILOG.CPLEX;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System;
using System.Windows.Forms;
using System.Linq;

namespace TSPCsharp
{
    public class Utility
    {
        //--------------------------------------------COMMON METHODS--------------------------------------------


        //Setting the current real residual time for Cplex and some relative parameters
        public static void MipTimelimit(Cplex cplex, Instance instance, Stopwatch clock)
        {
            double residualTime = instance.timeLimit - clock.ElapsedMilliseconds / 1000.0;

            if (residualTime < 0.0)
                residualTime = 0.0;

            cplex.SetParam(Cplex.IntParam.ClockType, 2);
            cplex.SetParam(Cplex.Param.TimeLimit, residualTime);                            // real time
            cplex.SetParam(Cplex.Param.DetTimeLimit, 1000 * cplex.GetParam(Cplex.Param.TimeLimit));			// ticks
        }

        //Creation of the process used to interect with GNUPlot
        public static Process InitProcess(Instance instance)
        {
            //Setting values to open Prompt
            ProcessStartInfo processStartInfo = new ProcessStartInfo("cmd.exe");
            processStartInfo.RedirectStandardInput = true;
            processStartInfo.RedirectStandardOutput = true;
            processStartInfo.UseShellExecute = false;

            //Executing the Prompt
            Process process = Process.Start(processStartInfo);

            Object Width = SystemInformation.VirtualScreen.Width;
            Object Height = SystemInformation.VirtualScreen.Height;

            //Enabling GNUPlot commands
            process.StandardInput.WriteLine("gnuplot\nset terminal wxt size {0},{1}\nset lmargin at screen 0.05\nset rmargin at screen 0.95\nset bmargin at screen 0.1\nset tmargin at screen 0.9");

            return process;
        }

        //Building initial model
        public static INumVar[] BuildModel(Cplex cplex, Instance instance, int nEdges)
        {
            //Il vettore contenente tutte le variabili viene creato
            INumVar[] x = new INumVar[(instance.nNodes - 1) * instance.nNodes / 2];

            //La variabili su cui viene definita una espressione lineare
            ILinearNumExpr expr = cplex.LinearNumExpr();

            //Solamente i lati delimitati dai noi (i,j) per cui i < j sono considerati
            for (int i = 0; i < instance.nNodes; i++)
            {
                for (int j = i + 1; j < instance.nNodes; j++)
                {
                    /*
                    * La funzione xPos determina il corretto indice
                    * da utilizzare per identificare il lato x(i+1, j+1)
                    * all'interno del vettore di variabili x
                    */
                    int position = xPos(i, j, instance.nNodes);

                    /*
                    * Creazione della variabile.
                    * Se necessario attivarne solo un numero limitato
                    * vengono tutte create inizialmente anche con 
                    * l'upper buond pari a 0
                    */
                    if (nEdges > 0)
                        x[position] = cplex.NumVar(0, 0, NumVarType.Bool, "x(" + (i + 1) + "," + (j + 1) + ")");
                    else
                        x[position] = cplex.NumVar(0, 1, NumVarType.Bool, "x(" + (i + 1) + "," + (j + 1) + ")");

                    /*
                    * Aggiunta del termine relativo al lato x(i+1, j+1)
                    * ad expr, per la funzione obiettivo il coefficiente
                    * è equivalente al costo del lato in questione
                    */
                    expr.AddTerm(x[position], Point.Distance(instance.coord[i], instance.coord[j], instance.edgeType));
                }
            }

            //Setting the optimality's function
            cplex.AddMinimize(expr);

            /*
            * Eventuale attivazione per tutti i nodi
            * dei soli (nEdges) lati meno costosi
            * ivi incidenti
            */
            if (nEdges > 0)
            {
                List<int>[] listArray = OrderEdgesComplete(instance);

                for (int i = 0; i < instance.nNodes; i++)
                {
                    for (int j = 0; j < nEdges; j++)
                    {
                        int position = xPos(i, listArray[i][j], instance.nNodes);

                        x[position].UB = 1;
                    }
                }
            }

            /*
            * Ogni nodo del grafo determina un proprio vincolo
            * che coinvolge tutti i lati in esso incidenti
            */
            for (int i = 0; i < instance.nNodes; i++)
            {
                //Reset di expr
                expr = cplex.LinearNumExpr();

                for (int j = 0; j < instance.nNodes; j++)
                {
                    /*
                    * L'espressione è la semplice somma di tutti i lati
                    * incidenti nel nodo quindi tutti quelli del tipo
                    * x(i+1,..) oppure x(..,i+1) validi
                    */
                    if (i != j)
                        expr.AddTerm(x[xPos(i, j, instance.nNodes)], 1);
                }

                //Aggiunta del vincolo al modello
                cplex.AddEq(expr, 2, "degree(" + (i + 1) + ")");
            }

            //Eventuale esportazione del modello matematico
            if (Program.VERBOSE >= 9)
                cplex.ExportModel(instance.inputFile + ".lp");

            //Il riferimento al vettore delle variabili x viene restituito
            return x;
        }


        //Used to evaluete the correct position to store and read the variables for the model
        public static int xPos(int i, int j, int nNodes)
        {
            if (i == j)
                return -1;

            if (i > j)
                return xPos(j, i, nNodes);

            return i * nNodes + j - (i + 1) * (i + 2) / 2;
        }

        //Print for GNUPlot
        public static void PrintGNUPlot(Process process, string name, int typeSol, double currentCost, double incumbentCost)
        {
            if (incumbentCost >= 0)
            {
                //typeSol == 1 => red lines, TypeSol == 0 => blue Lines
                if (typeSol == 0)
                    process.StandardInput.WriteLine("set style line 1 lc rgb '#0060ad' lt 1 lw 1 pt 5 ps 0.5\nset title \"Current best solution: " + incumbentCost + "   Current solution: " + currentCost + "\"\nplot '" + name + ".dat' with linespoints ls 1 notitle, '" + name + ".dat' using 1:2:3 with labels point pt 7 offset char 0,0.5 notitle\nset autoscale");
                else if (typeSol == 1)
                    process.StandardInput.WriteLine("set style line 1 lc rgb '#ad0000' lt 1 lw 1 pt 5 ps 0.5\nset title \"Current best solution: " + incumbentCost + "   Current solution: " + currentCost + "\"\nplot '" + name + ".dat' with linespoints ls 1 notitle, '" + name + ".dat' using 1:2:3 with labels point pt 7 offset char 0,0.5 notitle\nset autoscale");
            }
            else
            {
                //typeSol == 1 => red lines, TypeSol == 0 => blue Lines
                if (typeSol == 0)
                    process.StandardInput.WriteLine("set style line 1 lc rgb '#0060ad' lt 1 lw 1 pt 5 ps 0.5\nset title \"Current solution: " + currentCost + "\"\nplot '" + name + ".dat' with linespoints ls 1 notitle, '" + name + ".dat' using 1:2:3 with labels point pt 7 offset char 0,0.5 notitle\nset autoscale");
                else if (typeSol == 1)
                    process.StandardInput.WriteLine("set style line 1 lc rgb '#ad0000' lt 1 lw 1 pt 5 ps 0.5\nset title \"Current solution: " + currentCost + "\"\nplot '" + name + ".dat' with linespoints ls 1 notitle, '" + name + ".dat' using 1:2:3 with labels point pt 7 offset char 0,0.5 notitle\nset autoscale");
            }
        }


        //--------------------------------------------LOOP UTILITYES--------------------------------------------

        //Setting Upper Bounds of each cplex model's variable to 1
        public static void ResetVariables(INumVar[] x)
        {
            for (int i = 0; i < x.Length; i++)
                x[i].UB = 1;
        }

        //Initialization of the arrays used to keep track of the related components
        public static void InitCC(int[] cc)
        {
            for (int i = 0; i < cc.Length; i++)
            {
                cc[i] = i;
            }
        }

        //Updating the related components
        public static void UpdateCC(Cplex cplex, INumVar[] x, List<ILinearNumExpr> rcExpr, List<int> bufferCoeffRC, int[] relatedComponents, int i, int j)
        {
            if (relatedComponents[i] != relatedComponents[j])
            {
                //Scansione della componente connesse di ogni nodo
                for (int k = 0; k < relatedComponents.Length; k++)
                {
                    /*
                    * Eccetto per il nodo j che per questione algoritmica
                    * viene aggiornato per ultimo, tutti gli altri facenti
                    * parte della sua stessa componente connessa
                    * vengono trasferiti in quella del nodo i
                    */
                    if ((k != j) && (relatedComponents[k] == relatedComponents[j]))
                    {
                        relatedComponents[k] = relatedComponents[i];
                    }
                }
                //Infine anche la componente connessa relativa al nodo j è modificata
                relatedComponents[j] = relatedComponents[i];
            }
            else
            {
                //Una nuova espressione deve essere definita
                ILinearNumExpr expr = cplex.LinearNumExpr();

                //cnt memorizza quanti lati fanno parte del subtour trovato
                int cnt = 0;

                //Ogni nodo viene analizzato
                for (int h = 0; h < relatedComponents.Length; h++)
                {
                    /*
                    * Solo i nodi la relativa componente connessa
                    * è pari a quella che corrisponde ad un subtour
                    * proseguono nella analisi
                    */
                    if (relatedComponents[h] == relatedComponents[i])
                    {
                        /*
                        * Nel nodo di indice h incidono due lati facenti
                        * parte della attuale soluzione, per costruzione
                        * del tipo x(v,h) e x(h,w) con v<h<w e stesse
                        * componenti connesse di appartenenza.
                        * Grazie ad una analisi crescente degli indici
                        * dei nodi, è sufficiente cercare w ed aggiungere 
                        * x(h,w) ad expr in quanto il lato x(v,h) 
                        * è già stato aggiunto ad expr durante la
                        * v-esima iterazione del ciclo for
                        */
                        for (int k = h + 1; k < relatedComponents.Length; k++)
                        {
                            //Cerco il nodo w menzionato
                            if (relatedComponents[k] == relatedComponents[i])
                            {
                                /*
                                * Il lato x(h,w) menzionato viene così
                                * aggiunto ad expr con coefficiente uno
                                * secondo la struttura dei vincoli di
                                * subtour elimination
                                */
                                expr.AddTerm(x[xPos(h, k, relatedComponents.Length)], 1);
                            }
                        }
                        /*
                        * Ad ogni clausola if vera un lato viene
                        * aggiunto ad expr
                        */
                        cnt++;
                    }
                }
                /*
                * L'espressione definita ed il relativo numero di lati
                * che sono coinvolti al suo interno sono memorizzati
                * allo stesso indice all'interno dei rispettivi buffer
                */
                rcExpr.Add(expr);
                bufferCoeffRC.Add(cnt);
            }
        }



        //--------------------------------------------HEURISTIC UTILITYES--------------------------------------------

        public static PathGenetic NearestNeighbour(Instance instance, Random rnd, List<int>[] listArray)
        {
            // heuristicSolution is the path of the current heuristic solution to generate
            int[] heuristicSolution = new int[instance.nNodes];
            double distHeuristic = 0;

            int currentIndex = rnd.Next(instance.nNodes);
            int startindex = currentIndex;

            bool[] availableIndexes = new bool[instance.nNodes];

            availableIndexes[currentIndex] = true;

            for (int i = 0; i < instance.nNodes - 1; i++)
            {
                bool found = false;

                int plus = RndPlus(rnd);

                int nextIndex = listArray[currentIndex][0 + plus];

                do
                {
                    if (availableIndexes[nextIndex] == false)
                    {
                        heuristicSolution[currentIndex] = nextIndex;
                        distHeuristic += Point.Distance(instance.coord[currentIndex], instance.coord[nextIndex], instance.edgeType);
                        availableIndexes[nextIndex] = true;
                        currentIndex = nextIndex;
                        found = true;
                    }
                    else
                    {
                        plus++;
                        if (plus >= instance.nNodes - 1)
                        {
                            nextIndex = listArray[currentIndex][0];
                            plus = 0;
                        }
                        else
                            nextIndex = listArray[currentIndex][0 + plus];
                    }

                } while (!found);
            }

            heuristicSolution[currentIndex] = startindex;
            distHeuristic += Point.Distance(instance.coord[currentIndex], instance.coord[startindex], instance.edgeType);

            return new PathGenetic(heuristicSolution, distHeuristic);
        }

        //Generating a new heuristic solution for the Genetic Algorithm
        public static PathGenetic NearestNeighbourGenetic(Instance instance, Random rnd, bool rndStartPoint, List<int>[] listArray)
        {
            // heuristicSolution is the path of the current heuristic solution generate
            int[] heuristicSolution = new int[instance.nNodes];

            //List that contains all nodes belonging to the graph 
            List<int> availableNodes = new List<int>();
            int currenNode;

            //rndStartPoint define if the starting point is random or always the node 0 
            if (rndStartPoint)
                currenNode = rnd.Next(0, instance.nNodes);
            else
                currenNode = 0;

            heuristicSolution[0] = currenNode;
            availableNodes.Add(currenNode);

            for (int i = 1; i < instance.nNodes; i++)
            {
                bool found = false;
                int plus = RndGenetic(rnd);
                int nextNode = listArray[currenNode][plus];

                do
                {
                    //We control that the selected node has never been visited
                    if (availableNodes.Contains(nextNode) == false)
                    {
                        heuristicSolution[i] = nextNode;
                        availableNodes.Add(nextNode);
                        currenNode = nextNode;
                        found = true;
                    }
                    else
                    {
                        plus++;
                        if (plus >= instance.nNodes - 1)
                        {
                            nextNode = listArray[currenNode][0];
                            plus = 0;
                        }
                        else
                            nextNode = listArray[currenNode][0 + plus];
                    }

                } while (!found);
            }

            return new PathGenetic(heuristicSolution, instance);
        }

        public static List<int>[] OrderEdgesComplete(Instance instance)
        {
            //SL and L stores the information regarding the nearest edges for each node 
            List<itemList>[] SL = new List<itemList>[instance.nNodes];
            List<int>[] L = new List<int>[instance.nNodes];

            for (int i = 0; i < SL.Length; i++)
            {
                SL[i] = new List<itemList>();

                for (int j = 0; j < SL.Length; j++)
                {
                    //Simply adding each possible links with its distance
                    if (i != j)
                        SL[i].Add(new itemList(Point.Distance(instance.coord[i], instance.coord[j], instance.edgeType), j));
                }

                //Sorting the list
                SL[i] = SL[i].OrderBy(itemList => itemList.distance).ToList<itemList>();
                //Only the index of the nearest nodes are relevants
                L[i] = SL[i].Select(itemList => itemList.index).ToList<int>();
            }

            return L;
        }

        public static void SwapRoute(int c, int b, PathStandard pathG)
        {
            int from = b;
            int to = pathG.path[from];
            do
            {
                int tmpTo = pathG.path[to];
                pathG.path[to] = from;
                from = to;
                to = tmpTo;
            } while (from != c);
        }

        static int RndPlus(Random rnd)
        {
            double tmp = rnd.NextDouble();

            if (tmp < 0.9)
                return 0;
            else if (tmp < 0.99)
                return 1;
            else
                return 2;
        }

        static int RndGenetic(Random rnd)
        {
            double tmp = rnd.NextDouble();
            if (tmp < 0.5)
                return 0;
            else if (tmp < 0.60)
                return 1;
            else if (tmp < 0.80)
                return 2;
            else
                return 3;
        }

        public static void PrintHeuristicSolution(Instance instance, Process process, PathStandard pathG, double incumbentCost, int typeSol)
        {

            //Init the StreamWriter for the current solution
            StreamWriter file = new StreamWriter(instance.inputFile + ".dat", false);

            //Printing the optimal solution and the GNUPlot input file
            for (int i = 0; i < instance.nNodes; i++)
            {
                /*
                 *Current GNUPlot format is:
                 *-- previus link --
                 *<Blank line>
                 *Xi Yi <index(i)>
                 *Xj Yj <index(i)>
                 *<Blank line> 
                 *-- next link --
                */
                file.WriteLine(instance.coord[i].x + " " + instance.coord[i].y + " " + (i + 1));
                file.WriteLine(instance.coord[pathG.path[i]].x + " " + instance.coord[pathG.path[i]].y + " " + (pathG.path[i] + 1) + "\nEdges");
            }

            //GNUPlot input file needs to be closed
            file.Close();

            //Accessing GNUPlot to read the file
            if (Program.VERBOSE >= -1)
            {
                if (pathG.cost != incumbentCost)
                    PrintGNUPlot(process, instance.inputFile, typeSol, pathG.cost, incumbentCost);
                else
                    PrintGNUPlot(process, instance.inputFile, typeSol, pathG.cost, -1);
            }
        }

        public static PathGenetic GenerateChild(Instance instance, Random rnd, PathGenetic mother, PathGenetic father, List<int>[] listArray)
        {
            PathGenetic child;
            int[] pathChild = new int[instance.nNodes];

            //This variable defines the point of breaking the path of the father and that of the mother
            int crossover = (rnd.Next(0, instance.nNodes));

            for (int i = 0; i < instance.nNodes; i++)
            {
                if (i > crossover)
                    pathChild[i] = mother.path[i];
                else
                    pathChild[i] = father.path[i];
            }


            //With a probability of 0.01 we make a mutation
            if (rnd.Next(0, 101) == 100)
                Mutation(instance, rnd, pathChild);

            //The repair method ensures that the child is a permissible path
            child = Repair(instance, pathChild, listArray);


            if (ProbabilityTwoOpt(instance, rnd) == 1)
            {
                child.path = InterfaceForTwoOpt(child.path);

                TSP.TwoOpt(instance, child);

                child.path = Reverse(child.path);
            }
            return child;
        }

        public static List<PathGenetic> NextPopulation(Instance instance, int sizePopulation, List<PathGenetic> FatherGeneration, List<PathGenetic> ChildGeneration)
        {
            List<PathGenetic> nextGeneration = new List<PathGenetic>();
            //Data structure used to generate a population 
            List<int> roulette = new List<int>();
            Random rouletteValue = new Random();

            //List that contains all number raffled
            List<int> NumbersExtracted = new List<int>();
            bool find = false;
            int numberExtracted;

            for (int i = 0; i < ChildGeneration.Count; i++)
                FatherGeneration.Add(ChildGeneration[i]);

            //FillRoulette return the size of the roultette
            int upperExtremity = FillRoulette(roulette, FatherGeneration);

            for (int i = 0; i < sizePopulation; i++)
            {
                do
                {
                    find = true;
                    numberExtracted = rouletteValue.Next(0, upperExtremity);
                    //A path can't be extracted more than one time
                    if (NumbersExtracted.Contains(roulette[numberExtracted]) == false)
                    {
                        find = false;
                        NumbersExtracted.Add(roulette[numberExtracted]);

                        //We add to the next population the path having nRoulette equal to roulette[numberExtracted]
                        nextGeneration.Add(FatherGeneration.Find(x => x.nRoulette == roulette[numberExtracted]));
                    }
                } while (find);
            }
            return nextGeneration;
        }

        public static PathGenetic BestSolution(List<PathGenetic> percorsi, PathGenetic migliorCamminoAssoluto)
        {
            PathGenetic migliorCamminoGenerazione = migliorCamminoAssoluto;

            for (int i = 1; i < percorsi.Count; i++)
            {
                if (percorsi[i].cost < migliorCamminoGenerazione.cost)
                    migliorCamminoGenerazione = percorsi[i];
            }

            return migliorCamminoGenerazione;
        }

        static int FillRoulette(List<int> roulette, List<PathGenetic> CurrentGeneration)
        {
            int sizeRoulette = 0;
            int proportionalityConstant = Estimate(CurrentGeneration[0].fitness);

            for (int i = 0; i < CurrentGeneration.Count; i++)
            {
                int prob = (int)(CurrentGeneration[i].fitness * proportionalityConstant);
                CurrentGeneration[i].nRoulette = i;
                sizeRoulette += prob;
                for (int j = 0; j < prob; j++)
                    roulette.Add(i);
            }
            return sizeRoulette;
        }

        //Thi method create a constant k needed to the method FillRoulette
        static int Estimate(double sample)
        {
            int k = 1;
            while (sample < 100)
            {
                sample = sample * 10;
                k = k * 10;
            }
            return k;
        }

        public static void PrintGeneticSolution(Instance instance, Process process, PathGenetic Heuristic)
        {
            StreamWriter file = new StreamWriter(instance.inputFile + ".dat", false);

            for (int i = 0; i + 1 < instance.nNodes; i++)
            {
                int vertice1 = Heuristic.path[i];
                int vertice2 = Heuristic.path[i + 1];
                file.WriteLine(instance.coord[vertice1].x + " " + instance.coord[vertice1].y + " " + (vertice1 + 1));
                file.WriteLine(instance.coord[vertice2].x + " " + instance.coord[vertice2].y + " " + (vertice2 + 1) + "\nEdges");
            }
            file.WriteLine(instance.coord[Heuristic.path[0]].x + " " + instance.coord[Heuristic.path[0]].y + " " + (Heuristic.path[0] + 1));
            file.WriteLine(instance.coord[Heuristic.path[instance.nNodes - 1]].x + " " + instance.coord[Heuristic.path[instance.nNodes - 1]].y + " " + (Heuristic.path[instance.nNodes - 1] + 1) + "\nEdges");

            //GNUPlot input file needs to be closed
            file.Close();
            //Accessing GNUPlot to read the file
            if (Program.VERBOSE >= -1)
                PrintGNUPlot(process, instance.inputFile, 1, Heuristic.cost, -1);
        }

        //We modify randomly the path relative to the child
        static void Mutation(Instance instance, Random rnd, int[] pathChild)
        {
            int indexToModify = rnd.Next(0, pathChild.Length);
            int newValue = rnd.Next(0, instance.nNodes);
            pathChild[indexToModify] = newValue;
        }

        static PathGenetic Repair(Instance instance, int[] pathChild, List<int>[] listArray)
        {
            //This list contains the isolated nodes of the child
            List<int> isolatedNodes = new List<int>();

            //nearlestIsolatedNodes[i] contains the nearest node relative to isolatedNodes[i]
            List<int> nearlestIsolatedNodes = new List<int>();

            FindIsolatedNodes(instance, pathChild, isolatedNodes, nearlestIsolatedNodes);

            FindNearestNode(isolatedNodes, nearlestIsolatedNodes, listArray);

            //Is the path without nodes visited more times
            List<int> pathIncomplete = new List<int>();

            //Is the path that it is a solution of TSP
            int[] pathComplete = new int[instance.nNodes];

            for (int i = 0; i < instance.nNodes; i++)
            {
                if (pathIncomplete.Contains(pathChild[i]) == false)
                    pathIncomplete.Add(pathChild[i]);
            }

            int positionInsertNode = 0;

            for (int i = 0; i < pathIncomplete.Count; i++)
            {
                if (nearlestIsolatedNodes.Contains(pathIncomplete[i]))
                {
                    pathComplete[positionInsertNode] = pathIncomplete[i];
                    pathComplete[positionInsertNode + 1] = isolatedNodes[nearlestIsolatedNodes.IndexOf(pathIncomplete[i])];
                    positionInsertNode++;
                }
                else
                    pathComplete[positionInsertNode] = pathIncomplete[i];

                positionInsertNode++;
            }
            return new PathGenetic(pathComplete, instance);
        }

        //This list method calculate the isolated nodes of the child
        static void FindIsolatedNodes(Instance instance, int[] pathChild, List<int> isolatedNodes, List<int> nearestNeighIsolNode)
        {
            bool nodeIsVisited = false;
            for (int i = 0; i < instance.nNodes; i++)
            {
                for (int j = 0; j < instance.nNodes; j++)
                {
                    if (pathChild[j] == i)
                    {
                        nodeIsVisited = true;
                        //If the node is visited can exit to for
                        break;
                    }
                }

                //If the node has nevere been visited is a isolated noode
                if (nodeIsVisited == false)
                    isolatedNodes.Add(i);

                //Configure nodeIsVisited to its default value
                nodeIsVisited = false;
            }
        }

        static void FindNearestNode(List<int> isolatedNodes, List<int> nearestNeighIsolNode, List<int>[] listArray)
        {
            int nextNode = 0;
            int nearestNode = 0;
            bool find = true;

            for (int i = 0; i < isolatedNodes.Count; i++)
            {
                find = false;
                nextNode = 0;
                do
                {
                    nearestNode = listArray[isolatedNodes[i]][nextNode];

                    /*We don't want that the nearestNeighIsolNode relative to a isolated node is a isolated node or the
                    of another node*/
                    if (((isolatedNodes.Contains(nearestNode)) == false) && (nearestNeighIsolNode.Contains(nearestNode) == false))
                    {
                        nearestNeighIsolNode.Add(nearestNode);
                        find = false;
                    }
                    else
                    {
                        nextNode++;
                        find = true;
                    }
                } while (find);
            }
        }

        static int ProbabilityTwoOpt(Instance instance, Random rnd)
        {
            //The probability to applying TwoOpt is proportional to the number of nodes
            if (rnd.Next(1, instance.nNodes / 2) == 1)
                return 1;
            else
                return 0;
        }

        public static int[] InterfaceForTwoOpt(int[] path)
        {
            int[] inputTwoOpt = new int[path.Length];

            for (int i = 0; i < path.Length; i++)
            {
                for (int j = 0; j < path.Length; j++)
                {
                    if (j == path.Length - 1)
                    {
                        inputTwoOpt[i] = path[0];
                    }
                    else if (path[j] == i)
                    {
                        inputTwoOpt[i] = path[j + 1];
                        break;
                    }
                }
            }
            return inputTwoOpt;
        }

        public static int[] Reverse(int[] path)
        {
            int[] returnGenetic = new int[path.Length];

            returnGenetic[0] = path[0];

            for (int i = 1; i < path.Length; i++)
                returnGenetic[i] = path[returnGenetic[i - 1]];

            return returnGenetic;
        }


        //--------------------------------------------MATH HEURISTIC UTILITYES--------------------------------------------

        public static void ModifyModel(Instance instance, INumVar[] x, Random rnd, int percentageFixing, double[] values, List<int[]> fixedEdges)
        {
            for (int i = 0; i < x.Length; i++)//Inutile la prima volta
            {
                x[i].LB = 0;
                x[i].UB = 1;
            }

            for (int i = 0; i < x.Length; i++)
            {
                if ((values[i] == 1))
                {
                    if (RandomSelect(rnd, percentageFixing) == 1)
                    {
                        x[i].LB = 1;
                        fixedEdges.Add((xPosInv(i, instance.nNodes)));
                    }
                }
            }
        }

        static int RandomSelect(Random rnd, int percentageFixing)
        {
            if (rnd.Next(1, 10) < percentageFixing)
                return 1;
            else
                return 0;
        }

        static int[] xPosInv(int index, int nNodes)
        {
            for (int i = 0; i < nNodes; i++)
            {
                for (int j = i + 1; j < nNodes; j++)
                    if (xPos(i, j, nNodes) == index)
                        return new int[] { i, j };
            }

            return null;
        }

        public static void PreProcessing(Instance instance, INumVar[] x, List<int[]> fixedVariables)
        {
            int nodeLeft = 0;
            int nodeRight = 0;

            /* for (int i = 0; i < fixedVariables.Count; i++)
             {
                 int[] x = fixedVariables[i];
                 Console.WriteLine(x[0] + "---" + x[1]);
             }
             */
            for (int i = 0; i < fixedVariables.Count; i++)
            {
                int[] currentEdge = fixedVariables[i];

                nodeLeft = currentEdge[0];
                nodeRight = currentEdge[1];
                fixedVariables.Remove(currentEdge);


                for (int k = 0; k < fixedVariables.Count; k++)
                {
                    int[] fixedEdge = fixedVariables[k];
                    if (nodeLeft == fixedEdge[0])
                    {
                        nodeLeft = fixedEdge[1];
                        fixedVariables.Remove(fixedEdge);
                    }
                }
                for (int k = 0; k < fixedVariables.Count; k++)
                {
                    int[] fixedEdge = fixedVariables[k];
                    if (nodeLeft == fixedEdge[1])
                    {
                        nodeLeft = fixedEdge[0];
                        fixedVariables.Remove(fixedEdge);
                    }
                }

                for (int k = 0; k < fixedVariables.Count; k++)
                {
                    int[] fixedEdge = fixedVariables[k];
                    if (nodeRight == fixedEdge[0])
                    {
                        nodeRight = fixedEdge[1];
                        fixedVariables.Remove(fixedEdge);
                    }

                }

                for (int k = 0; k < fixedVariables.Count; k++)
                {
                    int[] fixedEdge = fixedVariables[k];
                    if (nodeRight == fixedEdge[1])
                    {
                        nodeRight = fixedEdge[0];
                        fixedVariables.Remove(fixedEdge);
                    }

                }
                if (nodeLeft != currentEdge[0] || nodeRight != currentEdge[1])
                    x[xPos(nodeLeft, nodeRight, instance.nNodes)].UB = 0;
            }
        }

        public static PathGenetic GenerateChildRins(Cplex cplex, Instance instance, Process process, INumVar[] x, PathGenetic mother, PathGenetic father)
        {
            PathGenetic child;
            int[] path = new int[instance.nNodes];

            int[] m = InterfaceForTwoOpt(mother.path);
            //PrintGeneticSolution(instance, process, mother.path);
            int[] f = InterfaceForTwoOpt(father.path);
            //PrintGeneticSolution(instance, process, father.path);

            for (int i = 0; i < m.Length; i++)
            {
                if (m[i] == f[i] || f[m[i]] == i)
                    x[xPos(i, m[i], m.Length)].LB = 1;
            }

            cplex.Solve();

            double[] actualX = cplex.GetValues(x);

            int tmp = 0;
            int[] available = new int[instance.nNodes];

            for (int i = 0; i < instance.nNodes - 1; i++)
            {
                for (int j = 0; j < instance.nNodes; j++)
                {
                    if (tmp != j && available[j] == 0)
                    {
                        int position = xPos(tmp, j, instance.nNodes);

                        if (actualX[position] >= 0.5)
                        {
                            path[tmp] = j;
                            tmp = j;
                            available[j] = 1;
                        }
                    }
                }
            }

            child = new PathGenetic(Reverse(path), instance);
            //PrintGeneticSolution(instance, process, child.path);

            for (int i = 0; i < m.Length; i++)
            {
                if (m[i] == f[i] || i == f[m[i]])
                    x[xPos(i, m[i], m.Length)].LB = 0;
            }

            return child;
        }

        static double[] ConvertIntArrayToDoubleArray(int[] adD)
        {
            return adD.Select(d => (double)d).ToArray();
        }
    }
}
