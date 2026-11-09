using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Reflection;
using System.ComponentModel;
namespace AppDataCleaner {
    static class SelfTest {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
        static void Assert(bool pass,string label) { if(!pass) throw new Exception(label); Console.WriteLine("PASS " + label); }
        public static int Run() {
            string temp = Path.Combine(Path.GetTempPath(),"AppDataCleaner-test-" + Guid.NewGuid());
            try {
                string[] boxes = new[] { "Local","LocalLow","Roaming" }.Select(x => Path.Combine(temp,x)).ToArray();
                foreach(string root in boxes) Directory.CreateDirectory(root);
                var scope = new Scope(boxes);
                Assert(scope.Roots.Count == 3,"multiple chosen roots");
                bool refused=false; try { scope.Add(Path.Combine(boxes[0],"..","Local")); } catch(IOException) { refused=true; }
                Assert(refused,"duplicate root refused");
                Directory.CreateDirectory(Path.Combine(boxes[0],"nested"));
                refused=false; try { scope.Add(Path.Combine(boxes[0],"nested")); } catch(IOException) { refused=true; }
                Assert(refused,"overlapping root refused");
                Directory.Delete(Path.Combine(boxes[0],"nested"));
                const long MB = 1024L * 1024; var now = new DateTime(2026,11,7);
                Func<long,DateTime,Entry> mk = (bytes,modified) => new Entry { Bytes = bytes, Modified = modified };
                var big = mk(1500 * MB, now); var small = mk(10 * MB, now);
                Assert(MainForm.Matches(big,1,1000,0,now) && !MainForm.Matches(small,1,1000,0,now),"size greater than");
                Assert(MainForm.Matches(small,2,1000,0,now) && !MainForm.Matches(big,2,1000,0,now) && !MainForm.Matches(mk(1000 * MB,now),2,1000,0,now),"size less than (strict)");
                Assert(MainForm.Matches(mk(0,now),3,0,0,now) && !MainForm.Matches(mk(100,now),3,0,0,now),"equal 0 MB means empty only");
                Assert(MainForm.Matches(mk(1000 * MB + MB / 3,now),3,1000,0,now) && !MainForm.Matches(mk(1001 * MB,now),3,1000,0,now),"equal N MB rounds to whole MB");
                Assert(MainForm.Matches(mk(5,now.AddMonths(-7)),0,0,6,now) && !MainForm.Matches(mk(5,now.AddMonths(-5)),0,0,6,now),"older than N months");
                Assert(!MainForm.Matches(mk(5,DateTime.MinValue),0,0,6,now) && MainForm.Matches(mk(5,DateTime.MinValue),0,0,0,now),"unknown modified time not counted as old");
                Assert(MainForm.Matches(mk(2000 * MB,now.AddYears(-1)),1,1000,6,now) && !MainForm.Matches(mk(2000 * MB,now),1,1000,6,now),"size and age combined");
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                Assert(Notes.ToKey(Path.Combine(home,"AppData","Foo")) == @"%USERPROFILE%\AppData\Foo" && Notes.ToKey(home + "2\\Foo") == home + "2\\Foo","user name hidden only under the real profile");
                Note note;
                Assert(Notes.ParseLine(@"%userprofile%\AppData\Foo | Foo 缓存 | 高",out note) == @"%USERPROFILE%\AppData\Foo" && note.ToString() == "Foo 缓存（把握：高）","note line parsed");
                Assert(Notes.ParseLine(@"| E:/Games/Bar | Bar 本体 | 中 |",out note) == @"E:\Games\Bar" && Notes.ParseLine(@"- `E:\Games\Bar | Bar | 低`",out note) == @"E:\Games\Bar","table, list and code wrappers tolerated");
                Assert(Notes.ParseLine(@"E:\Bar | 说明 | 很高",out note) == null && Notes.ParseLine(@"E:\Bar | a | b | 高",out note) == null && Notes.ParseLine(@"Bar | 说明 | 高",out note) == null && Notes.ParseLine(@"E:\Bar |  | 高",out note) == null,"malformed notes rejected");
                Notes.ParseLine("E:\\Bar | a\tb\u202E" + new string('长',500) + " | 高",out note);
                Assert(note.Text.Length == 200 && note.Text.StartsWith("ab长"),"control characters stripped and length capped");
                string noteFile = Path.Combine(temp,"merge-notes.txt");
                File.WriteAllLines(noteFile,new[] { "# 手写注释", @"E:\Old | 旧说明 | 低", "这行用户写坏了" });
                Notes.Merge(noteFile,new System.Collections.Generic.Dictionary<string,Note> { { @"e:\old", new Note { Text = "新说明", Confidence = "高" } }, { @"E:\New", new Note { Text = "新增", Confidence = "中" } } });
                string[] merged = File.ReadAllLines(noteFile);
                Assert(merged.Length == 4 && merged[0] == "# 手写注释" && merged[1] == @"E:\Old | 新说明 | 高" && merged[2] == "这行用户写坏了" && merged[3] == @"E:\New | 新增 | 中" && !File.Exists(noteFile + ".tmp"),"merge replaces, appends and keeps other lines");
                Assert(Notes.Load(noteFile).Count == 2 && Notes.Load(noteFile + ".missing").Count == 0,"notes file loaded");
                string dupFile = Path.Combine(temp,"dup-notes.txt");
                File.WriteAllLines(dupFile,new[] { @"E:\Dup | First | 低", "# 注释", @"E:\Other | keep | 中", @"e:\dup | Second | 低" });
                Notes.Merge(dupFile,new System.Collections.Generic.Dictionary<string,Note> { { @"E:\Dup", new Note { Text = "New", Confidence = "高" } } });
                string[] deduped = File.ReadAllLines(dupFile);
                Assert(deduped.SequenceEqual(new[] { @"E:\Dup | New | 高", "# 注释", @"E:\Other | keep | 中" }) && Notes.Load(dupFile)[@"E:\Dup"].Text == "New","duplicate paths merged so the new note takes effect");
                var pool = Enumerable.Range(0,5).Select(x => new Entry { Name = "u" + x, Description = "未知（把握：低）" })
                    .Concat(Enumerable.Range(0,28).Select(x => new Entry { Name = "e" + x }))
                    .Concat(Enumerable.Range(0,7).Select(x => new Entry { Name = "d" + x, Description = "【软件】Foo（把握：高）" })).ToArray();
                Entry[] undescribed = MainForm.Undescribed(pool,Notes.BatchSize);
                Assert(undescribed.Length == 30 && undescribed.Take(28).All(x => x.Name[0] == 'e') && undescribed.Skip(28).All(x => x.Name[0] == 'u') && undescribed[0].Name == "e0","empty notes picked before unknown, described skipped");
                string outside = Path.Combine(temp,"outside"); Directory.CreateDirectory(Path.Combine(outside,"ProductOutsideScan")); Directory.CreateDirectory(Path.Combine(outside,"inner","HiddenDeep"));
                string linkRoot = Path.Combine(temp,"link-root"); Directory.CreateDirectory(linkRoot);
                string junction = Path.Combine(linkRoot,"junction");
                using(var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe","/c mklink /J \"" + junction + "\" \"" + outside + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { mklink.StandardOutput.ReadToEnd(); mklink.WaitForExit(); }
                Assert(Directory.Exists(Path.Combine(junction,"ProductOutsideScan")),"test junction created");
                string linked = Notes.BuildBatches(new[] { new Entry { Path = junction, Modified = now } },Notes.BatchSize)[0];
                Assert(!linked.Contains("ProductOutsideScan") && linked.Contains("含链接，未读取"),"export does not follow a junction");
                string throughLink = Notes.BuildBatches(new[] { new Entry { Path = Path.Combine(junction,"inner"), Modified = now } },Notes.BatchSize)[0];
                Assert(!throughLink.Contains("HiddenDeep") && throughLink.Contains("含链接，未读取"),"export refuses a path under a junction");
                string missing = Notes.BuildBatches(new[] { new Entry { Path = Path.Combine(temp,"no-such-folder"), Modified = now } },Notes.BatchSize)[0];
                Assert(missing.Contains("内容：无法读取") && !missing.Contains("含链接"),"missing folder reported as unreadable");
                string clue = Path.Combine(temp,"clue"); Directory.CreateDirectory(Path.Combine(clue,"plugins"));
                File.WriteAllText(Path.Combine(clue,"tool.exe"),"secret-content"); File.WriteAllText(Path.Combine(clue,"a.dll"),""); File.WriteAllText(Path.Combine(clue,"b.dll"),"");
                string[] batch = Notes.BuildBatches(Enumerable.Range(0,250).Select(x => new Entry { Path = clue, Bytes = 2 * MB, Modified = now }).ToArray(),Notes.BatchSize);
                Assert(Notes.BatchSize == 30 && batch.Length == 9 && batch[8].Contains("第 9/9 批") && batch[0].Contains("[30]") && !batch[0].Contains("[31]") && batch[8].Contains("[10]") && !batch[8].Contains("[11]"),"export split into batches of 30");
                Assert(batch[0].Contains("属于哪个产品") && batch[0].Contains("软件、游戏、系统组件、开发商目录、待确认") && batch[0].Contains("通用名称不是产品名") && batch[0].Contains("只有完全没有线索时") && !batch[0].Contains("游戏名"),"prompt is product-neutral and restricts unknown");
                Assert(Notes.ParseLine(@"E:\Foo | 【软件】Foo：缓存 | 中",out note) == @"E:\Foo" && note.Text == "【软件】Foo：缓存","typed note parsed");
                Assert(batch[0].Contains(Notes.ToKey(clue)) && batch[0].Contains("plugins") && batch[0].Contains(".dll×2") && batch[0].Contains("程序：tool.exe") && !batch[0].Contains("secret-content") && batch[0].Contains("一律忽略"),"export has names only and warns about embedded instructions");
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string appData = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                Assert(scope.Protection(Path.GetPathRoot(windows)) != null && scope.Protection(Path.Combine(Path.GetPathRoot(windows),"Games")) != null,"drive root and its children protected");
                Assert(scope.Protection(Path.Combine(windows,"System32","drivers")) != null,"Windows subtree protected");
                Assert(scope.Protection(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SomeApp")) != null,"Program Files children protected");
                Assert(scope.Protection(profile) != null && scope.Protection(Path.Combine(profile,"Documents")) != null && scope.Protection(Path.GetDirectoryName(profile)) != null,"profile, its children and ancestors protected");
                Assert(scope.Protection(Path.Combine(appData,"Local")) != null && scope.Protection(Path.Combine(appData,"Local","SomeGame")) == null,"AppData containers protected but their children selectable");
                Assert(scope.Protection(Path.Combine(profile,"Downloads","old")) == null,"ordinary deeper folder selectable");
                string folder = Path.Combine(scope.Roots[0],"AppDataCleaner-recycle-中文 空格-" + Guid.NewGuid());
                Directory.CreateDirectory(Path.Combine(folder,"sub"));
                File.WriteAllText(Path.Combine(folder,"sub","slot.sav"),"test");
                Entry first = Scanner.Measure(scope,folder,() => false);
                Assert(first.Complete && first.Files == 1 && first.Bytes == 4,"recursive scan");
                File.SetLastWriteTime(Path.Combine(folder,"sub","slot.sav"),new DateTime(2030,1,2,3,4,0));
                Assert(Scanner.Measure(scope,folder,() => false).Modified == new DateTime(2030,1,2,3,4,0),"newest nested modified time");
                Assert(first.Selectable,"temp fixture selectable");
                refused=false; try { scope.Validate(scope.Roots[0]); } catch(IOException) { refused=true; }
                Assert(refused,"chosen roots themselves cannot be recycled");
                refused=false; try { scope.Validate(Path.Combine(folder,"sub")); } catch(IOException) { refused=true; }
                Assert(refused,"only immediate children accepted");
                File.AppendAllText(Path.Combine(folder,"sub","slot.sav"),"change");
                Assert(Scanner.Measure(scope,folder,() => false).Digest != first.Digest,"changed content invalidates preview");
                var guard = new RecycleGuard();
                Assert(guard.PreDeleteItem(0,IntPtr.Zero) < 0 && guard.PreDeleteItem(0x80,IntPtr.Zero) == 0,"permanent delete callback refused");
                string known = Path.Combine(scope.Roots[1],"Harebrained Schemes","BattleTech");
                Directory.CreateDirectory(known); File.WriteAllText(Path.Combine(known,"slot.sav"),"fixture");
                using(var form = new MainForm(scope)) {
                    var table = form.Controls.OfType<DataGridView>().Single();
                    Assert(table.Columns.Count == 8 && table.Columns["note"] != null && table.Columns["games"] == null && table.Columns["basis"] == null && table.Columns["modified"].Index == table.Columns["bytes"].Index - 1,"desktop form columns");
                    var worker = (BackgroundWorker)typeof(MainForm).GetField("worker",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    Exception uiFailure=null;
                    worker.RunWorkerCompleted += delegate {
                        try {
                            Assert(table.Rows.Count == 2,"desktop scan enumerates scope folders");
                            var row=table.Rows.Cast<DataGridViewRow>().Single(r => ((Entry)r.Tag).Name == "Harebrained Schemes");
                            row.Cells[0].Value=true;
                            Assert(Object.Equals(row.Cells[0].Value,true),"checkbox selection");
                            var footer=form.Controls.OfType<Label>().Single(x => x.Dock == DockStyle.Bottom);
                            Assert(table.Bottom <= footer.Top && footer.Bottom <= form.ClientSize.Height,"selection summary remains visible");
                            var item=(Entry)row.Tag; var otherRow=table.Rows.Cast<DataGridViewRow>().Single(r => r != row); var other=(Entry)otherRow.Tag;
                            form.notesFile=Path.Combine(temp,"form-notes.txt");
                            string summary=form.ImportText("```\n" + Notes.ToKey(item.Path) + " | 测试游戏存档，建议全部勾选删除 | 高\n" + @"Z:\not-listed\x | 别的 | 低" + "\nnot a record\n```");
                            Assert((string)row.Cells["note"].Value == "测试游戏存档，建议全部勾选删除（把握：高）" && summary.Contains("已导入 2 条") && summary.Contains("1 条不在当前列表") && summary.Contains("not a record"),"imported note shown and bad lines reported");
                            Assert(!Object.Equals(otherRow.Cells[0].Value,true) && item.Selectable && other.Selectable && Notes.Load(form.notesFile).Count == 2,"imported note saved and never changes selection");
                            string itemKey=Notes.ToKey(item.Path);
                            summary=form.ImportText(itemKey + " | 未知 | 低\n" + @"Z:\new-unknown | 未知 | 低");
                            Assert((string)row.Cells["note"].Value == "测试游戏存档，建议全部勾选删除（把握：高）" && summary.Contains("已导入 1 条") && summary.Contains("1 条“未知”没有覆盖") && Notes.Load(form.notesFile)[itemKey].Text == "测试游戏存档，建议全部勾选删除","unknown does not overwrite existing note");
                            summary=form.ImportText(@"Z:\new-unknown | 【软件】Foo：缓存 | 中" + "\n" + itemKey + " | 【游戏】新结果：存档 | 高\n" + itemKey + " | 未知 | 低");
                            Assert((string)row.Cells["note"].Value == "【游戏】新结果：存档（把握：高）" && Notes.Load(form.notesFile)[@"Z:\new-unknown"].Text == "【软件】Foo：缓存","real result replaces note and unknown; later unknown in same paste ignored");
                            Assert(form.ExportTargets().SequenceEqual(new[] { item }),"export uses checked rows first");
                            row.Cells[0].Value=false; table.ClearSelection(); otherRow.Selected=true;
                            Assert(form.ExportTargets().SequenceEqual(new[] { other }),"export falls back to highlighted rows");
                            table.ClearSelection(); row.Cells[0].Value=true;
                            Assert(form.PickUndescribed() == 1 && otherRow.Selected && !row.Selected && !Object.Equals(otherRow.Cells[0].Value,true),"pick highlights undescribed rows without ticking them");
                            Assert(form.ExportTargets().SequenceEqual(new[] { other }),"auto-picked rows win over existing recycle ticks");
                            row.Selected=true;
                            Assert(form.ExportTargets().SequenceEqual(new[] { item }),"manual selection returns export to ticked rows");
                            table.ClearSelection(); otherRow.Visible=false;
                            Assert(form.PickUndescribed() == 0 && table.SelectedRows.Count == 0,"pick ignores rows hidden by filters");
                            otherRow.Visible=true; table.ClearSelection();
                            string preview=Environment.GetEnvironmentVariable("APPDATA_CLEANER_TEST_IMAGE");
                            if(!String.IsNullOrEmpty(preview)) using(var image=new System.Drawing.Bitmap(form.Width,form.Height)) {
                                form.DrawToBitmap(image,form.ClientRectangle); image.Save(preview);
                            }
                        } catch(Exception e) { uiFailure=e; }
                        finally { form.Close(); }
                    };
                    form.Shown += delegate { typeof(MainForm).GetMethod("StartScan",BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form,null); };
                    Application.Run(form);
                    if(uiFailure != null) throw uiFailure;
                }
                Recycler.Move(folder.Replace('\\','/') + "/");
                Assert(!Directory.Exists(folder),"entire folder recycled with catalog-style path");
                var keyScope = new Scope(boxes);
                using(var form = new MainForm(keyScope)) {
                    var list = (ListBox)typeof(MainForm).GetField("rootList",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    MethodInfo key = typeof(Control).GetMethod("OnKeyDown",BindingFlags.Instance | BindingFlags.NonPublic);
                    key.Invoke(list,new object[] { new KeyEventArgs(Keys.Control | Keys.A) });
                    Assert(list.SelectedItems.Count == 3,"Ctrl+A selects all roots");
                    list.ClearSelected(); list.SetSelected(0,true); list.SetSelected(2,true);
                    key.Invoke(list,new object[] { new KeyEventArgs(Keys.Delete) });
                    Assert(keyScope.Roots.Count == 1 && list.Items.Count == 1 && (string)list.Items[0] == keyScope.Roots[0],"Delete removes all selected roots");
                }
                using(var form = new MainForm(new Scope(boxes))) {
                    var list = (ListBox)typeof(MainForm).GetField("rootList",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    form.CreateControl(); form.PerformLayout();
                    int h = list.ItemHeight, blank = list.ClientSize.Height - 3;
                    Assert(list is BandListBox && blank > h * 3 && list.IndexFromPoint(10,blank) == ListBox.NoMatches,"band list has blank area below roots");
                    Func<int,int,IntPtr> at = (x,y) => (IntPtr)((y << 16) | x);
                    SendMessage(list.Handle,0x201,(IntPtr)1,at(10,blank));          // 空白处按下
                    SendMessage(list.Handle,0x200,(IntPtr)1,at(10,h + h / 2));      // 向上拖到第 2 项
                    SendMessage(list.Handle,0x202,IntPtr.Zero,at(10,h + h / 2));    // 松开
                    Assert(list.SelectedIndices.Cast<int>().SequenceEqual(new[] { 1,2 }),"dragging up from blank area selects boxed roots");
                    SendMessage(list.Handle,0x201,(IntPtr)1,at(10,blank));
                    SendMessage(list.Handle,0x202,IntPtr.Zero,at(10,blank));
                    Assert(list.SelectedItems.Count == 0,"click on blank area clears selection");
                }
                string deep = Path.Combine(scope.Roots[2],"long-path");
                string leaf = deep; while(leaf.Length < 320) leaf = Path.Combine(leaf,"segment-0123456789");
                Directory.CreateDirectory(leaf); File.WriteAllText(Path.Combine(leaf,"deep.sav"),"long");
                Entry longEntry = Scanner.Measure(scope,deep,() => false);
                Assert(longEntry.Complete && longEntry.Files == 1 && longEntry.Bytes == 4,"path over 260 chars scanned (" + longEntry.State + ")");
                // 不调用 Recycler：回收站放不下，系统会弹出永久删除确认框
                Assert(!longEntry.Selectable && longEntry.State.Contains("260"),"folder with path over 260 chars not selectable");
                string emptyLong = Path.Combine(scope.Roots[2],new string('e',220));
                Directory.CreateDirectory(emptyLong);
                Entry emptyEntry = Scanner.Measure(scope,emptyLong,() => false);
                Assert(emptyLong.Length >= 260 && emptyEntry.Complete && emptyEntry.Files == 0 && !emptyEntry.Selectable,"empty folder whose own path is over 260 chars not selectable");
                string gone = Path.Combine(temp,"gone-root"); Directory.CreateDirectory(gone);
                var failScope = new Scope(new[] { boxes[1], gone }); Directory.Delete(gone);
                using(var form = new MainForm(failScope)) {
                    var table = form.Controls.OfType<DataGridView>().Single();
                    var footer = form.Controls.OfType<Label>().Single(x => x.Dock == DockStyle.Bottom);
                    var worker = (BackgroundWorker)typeof(MainForm).GetField("worker",BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                    string finalText = null; int listed = -1;
                    worker.RunWorkerCompleted += delegate { finalText = footer.Text; listed = table.Rows.Count; form.Close(); };
                    form.Shown += delegate { typeof(MainForm).GetMethod("StartScan",BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form,null); };
                    Application.Run(form);
                    Assert(finalText != null && finalText.StartsWith("扫描不完整，1 个扫描目录读取失败") && finalText.Contains(gone) && listed > 0,"failed scan root reported after scan; other roots still listed");
                }
                string locked = Path.Combine(scope.Roots[1],"locked");
                Directory.CreateDirectory(locked); string file=Path.Combine(locked,"locked.sav"); File.WriteAllText(file,"keep");
                using(var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None)) {
                    refused=false; try { Recycler.Move(locked); } catch(IOException) { refused=true; }
                    Assert(refused && File.Exists(file),"locked file stops recycling and remains present");
                }
                Assert(File.ReadAllText(file) == "keep","locked file content preserved");
                Console.WriteLine("SELF TEST OK"); return 0;
            } catch(Exception e) { Console.WriteLine(e); return 1; }
            finally {
                // .NET 递归删除遇到目录联接会失败，先单独拆掉联接本身（不碰目标）
                string testJunction = Path.Combine(temp,"link-root","junction");
                if(Directory.Exists(testJunction)) Directory.Delete(testJunction);
                // Only this newly created, absolute temporary fixture is removed.
                if(Directory.Exists(temp)) Directory.Delete(temp,true);
            }
        }
    }
}
