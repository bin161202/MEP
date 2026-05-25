using Autodesk.Revit.DB;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.Data;
using System.IO;
using ExcelDataReader;
using Excel = Microsoft.Office.Interop.Excel;
using Window = System.Windows.Window;
using Microsoft.Office.Interop.Excel;
using System.Runtime.InteropServices;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using System;
using System.Text;
using View = Autodesk.Revit.DB.View;
using Parameter = Autodesk.Revit.DB.Parameter;

namespace MEPAuto.SheetFromExcel.Views
{
    public class SheetData
    {
        public string SheetNumber { get; set; }
        public string SheetName { get; set; }
        public string ViewName { get; set; }
        public string LevelName { get; set; }
        public string ScopeBoxName { get; set; }
        public string ViewTemplateName { get; set; }
        public string TitleOnSheet { get; set; }
        public string SheetGroup { get; set; }
    }

    public partial class SheetFromExcelWindow : Window
    {
        static SheetFromExcelWindow()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        private Document doc;
        public SheetFromExcelWindow(Document doc)
        {
            InitializeComponent();
            this.doc = doc;
            cbb_TitleBlocks.ItemsSource = GetListTitleBlocks();
            cbb_TitleBlocks.SelectedIndex = 0;
        }

        private void bt_Browse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Chọn file Excel";
            dialog.Filter = "Excel Files| *xls; *xlsx; *xlsm";

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                tb_FilePath.Text = dialog.FileName;
            }
            else tb_FilePath.Text = "";
        }

        private List<string> GetListTitleBlocks()
        {
            var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .Cast<FamilySymbol>()
                        .ToList();

            var listNames = (collector.Select(x => $"{x.FamilyName}: {x.Name}")).ToList();
            listNames.Sort();
            return listNames;
        }

        private ElementId GetBlockId(string blockName)
        {
            var collector = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_TitleBlocks)
                        .WhereElementIsElementType()
                        .Cast<FamilySymbol>()
                        .ToList();
            var id = collector.Find(x => $"{x.FamilyName}: {x.Name}" == blockName)?.Id;
            return id;
        }

        private void bt_Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private List<SheetData> ReadExcelData(string filePath)
        {
            var result = new List<SheetData>();
            using (FileStream fileStream = File.Open(filePath, FileMode.Open, FileAccess.Read))
            {
                using (var reader = ExcelReaderFactory.CreateReader(fileStream))
                {
                    var data = reader.AsDataSet();
                    if (data == null || data.Tables.Count == 0) return result;
                    var table = data.Tables[0];
                    if (table.Rows.Count < 2) return result;

                    for (int i = 1; i < table.Rows.Count; i++)
                    {
                        var row = table.Rows[i];
                        var sd = new SheetData
                        {
                            SheetNumber = row[0]?.ToString(),
                            SheetName = row[1]?.ToString(),
                            ViewName = row[2]?.ToString(),
                            LevelName = row[3]?.ToString(),
                            ScopeBoxName = row[4]?.ToString(),
                            ViewTemplateName = row[5]?.ToString(),
                            TitleOnSheet = row[6]?.ToString(),
                            SheetGroup = row[7]?.ToString()
                        };
                        if (string.IsNullOrWhiteSpace(sd.SheetNumber) && string.IsNullOrWhiteSpace(sd.SheetName) && string.IsNullOrWhiteSpace(sd.ViewName))
                            continue;
                        result.Add(sd);
                    }
                }
            }
            return result;
        }

        private void bt_Ok_Click(object sender, RoutedEventArgs e)
        {
            string blockName = cbb_TitleBlocks.SelectedValue?.ToString();
            ElementId blockId = GetBlockId(blockName);

            string filePath = tb_FilePath.Text;
            if (string.IsNullOrEmpty(filePath))
            {
                MessageBox.Show("Chọn file excel hoặc copy/paste đường dẫn!", "Thông báo");
                return;
            }
            if (filePath.Contains("\"")) filePath = filePath.Replace("\"", "");

            var excelData = ReadExcelData(filePath);
            if (excelData == null || excelData.Count == 0)
            {
                MessageBox.Show("File Excel không có dữ liệu hợp lệ!", "Thông báo");
                return;
            }

            CloseExcelFile(filePath);

            List<string> duplicateSheets = new List<string>();
            List<SheetData> validSheets = new List<SheetData>();
            foreach (var row in excelData)
            {
                if (!string.IsNullOrEmpty(row.SheetNumber) && IsSheetNumberExists(row.SheetNumber))
                {
                    duplicateSheets.Add(row.SheetNumber);
                }
                else
                {
                    validSheets.Add(row);
                }
            }
            if (duplicateSheets.Count > 0)
            {
                string msg = "Các Sheet Number sau đã tồn tại và được bỏ qua:\n" + string.Join("\n", duplicateSheets);
                MessageBox.Show(msg, "Sheet Number trùng lặp", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            validSheets = validSheets
                .GroupBy(s => s.SheetNumber, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            foreach (var row in validSheets)
            {
                if (!string.IsNullOrWhiteSpace(row.ViewName) && !string.IsNullOrWhiteSpace(row.SheetName))
                {
                    if (row.ViewName.Trim().Equals(row.SheetName.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        row.ViewName = row.ViewName.Trim() + "_S";
                    }
                }
            }

            int soFloorPlanTaoMoi = 0;
            int soScopeBoxGan = 0;
            int soViewTemplateGan = 0;
            var allLevels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .ToList();
            var floorPlanType = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);
            var scopeBoxDefault = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                .WhereElementIsNotElementType()
                .FirstOrDefault();

            var floorPlanDict = new Dictionary<string, ViewPlan>(StringComparer.OrdinalIgnoreCase);

            using (Transaction trans = new Transaction(doc, "Tạo Floor Plan từ Excel"))
            {
                trans.Start();
                foreach (var row in validSheets)
                {
                    if (string.IsNullOrWhiteSpace(row.ViewName) || string.IsNullOrWhiteSpace(row.LevelName))
                        continue;

                    var level = allLevels.FirstOrDefault(l => l.Name.Equals(row.LevelName.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (level == null) continue;

                    var existing = new FilteredElementCollector(doc)
                        .OfClass(typeof(ViewPlan))
                        .Cast<ViewPlan>()
                        .FirstOrDefault(v => v.ViewType == ViewType.FloorPlan &&
                                             v.GenLevel?.Id == level.Id &&
                                             v.Name.Equals(row.ViewName.Trim(), StringComparison.OrdinalIgnoreCase));

                    ViewPlan floorPlan = existing;
                    if (floorPlan == null && floorPlanType != null)
                    {
                        floorPlan = ViewPlan.Create(doc, floorPlanType.Id, level.Id);
                        floorPlan.Name = row.ViewName.Trim();
                        soFloorPlanTaoMoi++;
                    }
                    if (floorPlan != null)
                    {
                        if (scopeBoxDefault != null)
                        {

                            var param = floorPlan.get_Parameter(BuiltInParameter.VIEWER_VOLUME_OF_INTEREST_CROP);
                            if (param != null && !param.IsReadOnly)
                            {
                                param.Set(scopeBoxDefault.Id);
                                floorPlan.CropBoxActive = true;
                                floorPlan.CropBoxVisible = true;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(row.ViewTemplateName))
                        {
                            var template = new FilteredElementCollector(doc)
                                .OfClass(typeof(Autodesk.Revit.DB.View))
                                .Cast<Autodesk.Revit.DB.View>()
                                .FirstOrDefault(vt => vt.IsTemplate && vt.Name == row.ViewTemplateName.Trim() && vt.ViewType == ViewType.FloorPlan);

                            if (template != null && floorPlan.ViewTemplateId != template.Id)
                            {
                                floorPlan.ViewTemplateId = template.Id;
                                soViewTemplateGan++;
                            }
                        }
                        floorPlanDict[row.ViewName.Trim()] = floorPlan;
                    }
                }
                trans.Commit();
            }

            List<(Viewport viewport, SheetData data)> viewportList = new List<(Viewport, SheetData)>();
            List<SheetData> needDraftingLine = new List<SheetData>();

            using (TransactionGroup tg = new TransactionGroup(doc, "Tạo Sheet, View, Viewport và gán Scope Box"))
            {
                tg.Start();
                try
                {
                    using (var t = new Transaction(doc, "Tạo Sheet, View, Viewport và gán Scope Box"))
                    {
                        t.Start();
                        foreach (var row in validSheets)
                        {
                            bool needLine = false;
                            Autodesk.Revit.DB.View view = null;

                            if (!string.IsNullOrWhiteSpace(row.ViewName))
                            {
                                var existingView = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Autodesk.Revit.DB.View))
                                    .Cast<Autodesk.Revit.DB.View>()
                                    .FirstOrDefault(v => v.Name.Equals(row.ViewName.Trim(), StringComparison.OrdinalIgnoreCase) && !v.IsTemplate);

                                if (existingView != null)
                                {
                                    view = existingView;
                                }
                                else if (floorPlanDict.TryGetValue(row.ViewName.Trim(), out var fp))
                                {
                                    view = fp;
                                }
                                else if (string.IsNullOrWhiteSpace(row.LevelName) && string.IsNullOrWhiteSpace(row.ViewTemplateName))
                                {
                                    needLine = true;
                                }
                                else
                                {
                                    view = CreateOrGetView(row.ViewName.Trim(), row.LevelName);
                                    if (view == null)
                                        needLine = true;
                                }
                            }
                            else
                            {
                                needLine = true;
                            }

                            if (needLine)
                            {
                                needDraftingLine.Add(row);
                                continue;
                            }

                            ViewSheet vs = ViewSheet.Create(doc, blockId);
                            if (vs == null) continue;

                            if (!string.IsNullOrEmpty(row.SheetNumber))
                            {
                                try
                                {
                                    vs.SheetNumber = row.SheetNumber;
                                }
                                catch (Autodesk.Revit.Exceptions.ArgumentException)
                                {
                                    throw new Exception($"Sheet Number đã tồn tại: {row.SheetNumber}");
                                }
                            }
                            if (!string.IsNullOrEmpty(row.SheetName))
                                vs.Name = row.SheetName;

                            if (!string.IsNullOrEmpty(row.SheetGroup))
                                SetParameters(vs, "Sheet Group", row.SheetGroup);
                            if (!string.IsNullOrEmpty(row.TitleOnSheet))
                                SetParameters(vs, "Title on Sheet", row.TitleOnSheet);

                            if (!string.IsNullOrEmpty(row.ViewTemplateName) && view != null && view.ViewType != ViewType.FloorPlan)
                            {
                                var template = new FilteredElementCollector(doc)
                                    .OfClass(typeof(Autodesk.Revit.DB.View))
                                    .Cast<Autodesk.Revit.DB.View>()
                                    .Where(vt => vt.IsTemplate && vt.Name == row.ViewTemplateName)
                                    .FirstOrDefault(vt => vt.ViewType == view.ViewType);

                                if (template != null && view.ViewTemplateId != template.Id)
                                    view.ViewTemplateId = template.Id;
                            }

                            Viewport viewport = null;
                            if (vs != null && view != null && Viewport.CanAddViewToSheet(doc, vs.Id, view.Id))
                            {
                                viewport = Viewport.Create(doc, vs.Id, view.Id, new XYZ(0, 0, 0));

                                Parameter annoCropParam = view.get_Parameter(BuiltInParameter.VIEWER_ANNOTATION_CROP_ACTIVE);
                                if (annoCropParam != null && !annoCropParam.IsReadOnly)
                                {
                                    annoCropParam.Set(1);
                                }

                                if (!string.IsNullOrEmpty(row.ScopeBoxName))
                                if (!string.IsNullOrEmpty(row.ScopeBoxName))
                                {
                                    var scopeBox = new FilteredElementCollector(doc)
                                        .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                                        .WhereElementIsNotElementType()
                                        .FirstOrDefault(se => se.Name.Equals(row.ScopeBoxName, StringComparison.InvariantCultureIgnoreCase));

                                    if (scopeBox != null)
                                    {
                                        var param = viewport.LookupParameter("Scope Box");
                                        if (param != null && !param.IsReadOnly)
                                        {
                                            param.Set(scopeBox.Id);
                                        }
                                    }
                                    else
                                    {
                                        throw new Exception($"Không tìm thấy Scope Box: {row.ScopeBoxName}");
                                    }
                                }

                                if (!string.IsNullOrEmpty(row.TitleOnSheet))
                                {
                                    var titleParam = viewport.LookupParameter("Title on Sheet");
                                    if (titleParam != null)
                                    {
                                        titleParam.Set(row.TitleOnSheet);
                                    }
                                }

                                viewportList.Add((viewport, row));
                            }
                        }
                        t.Commit();
                    }

                    UIDocument uidoc = new UIDocument(doc);
                    var points = PickRectangle(uidoc);
                    if (points == null)
                    {
                        throw new Exception("Bạn đã hủy thao tác chọn điểm!");
                    }

                    XYZ pt1 = points.Item1;
                    XYZ pt2 = points.Item2;
                    double z = pt1.Z;
                    XYZ center = new XYZ(
                        (pt1.X + pt2.X) / 2,
                        (pt1.Y + pt2.Y) / 2,
                        z);

                    if (needDraftingLine.Count > 0)
                    {
                        using (var t = new Transaction(doc, "Tạo Drafting View với line 1000mm"))
                        {
                            t.Start();
                            foreach (var row in needDraftingLine)
                            {
                                string desiredName = string.IsNullOrWhiteSpace(row.ViewName) ? "Drafting View" : row.ViewName.Trim();
                                string finalName = desiredName;
                                int suffixIndex = 1;

                                while (new FilteredElementCollector(doc)
                                    .OfClass(typeof(Autodesk.Revit.DB.View))
                                    .Cast<Autodesk.Revit.DB.View>()
                                    .Any(v => v.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    finalName = $"{desiredName} ({suffixIndex})";
                                    suffixIndex++;
                                }

                                var draftingView = CreateDraftingView(doc, finalName);
                                if (draftingView == null) continue;

                                ViewSheet vs = ViewSheet.Create(doc, blockId);
                                if (vs == null) continue;
                                if (!string.IsNullOrEmpty(row.SheetNumber))
                                {
                                    try
                                    {
                                        vs.SheetNumber = row.SheetNumber;
                                    }
                                    catch (Autodesk.Revit.Exceptions.ArgumentException)
                                    {
                                        continue;
                                    }
                                }
                                if (!string.IsNullOrEmpty(row.SheetName))
                                    vs.Name = row.SheetName;

                                if (!string.IsNullOrEmpty(row.SheetGroup))
                                    SetParameters(vs, "Sheet Group", row.SheetGroup);
                                if (!string.IsNullOrEmpty(row.TitleOnSheet))
                                    SetParameters(vs, "Title on Sheet", row.TitleOnSheet);

                                if (Viewport.CanAddViewToSheet(doc, vs.Id, draftingView.Id))
                                {
                                    var viewport = Viewport.Create(doc, vs.Id, draftingView.Id, center);
                                    if (!string.IsNullOrEmpty(row.TitleOnSheet))
                                    {
                                        var titleParam = viewport.LookupParameter("Title on Sheet");
                                        if (titleParam != null)
                                        {
                                            titleParam.Set(row.TitleOnSheet);
                                        }
                                        AdjustViewportTitle(viewport, row.TitleOnSheet);
                                    }


                                }


                            }
                            t.Commit();
                        }
                    }

                    int movedCount = 0;
                    using (var t = new Transaction(doc, "Di chuyển Viewport vào giữa 2 điểm và căn giữa title"))
                    {
                        t.Start();
                        foreach (var item in viewportList)
                        {
                            var viewport = item.viewport;
                            var row = item.data;
                            if (viewport != null)
                            {
                                var boxCenter = viewport.GetBoxCenter();
                                var translation = center.Subtract(boxCenter);
                                ElementTransformUtils.MoveElement(doc, viewport.Id, translation);
                                AdjustGrids(doc,viewport);
                                movedCount++;

                                if (!string.IsNullOrEmpty(row.TitleOnSheet))
                                {
                                    AdjustViewportTitle(viewport, row.TitleOnSheet);
                                }
                            }

                        }
                        t.Commit();
                    }

                    tg.Assimilate();
                    string msg = $"{viewportList.Count + needDraftingLine.Count} sheet đã được tạo!\n{movedCount} viewport đã được di chuyển vào giữa 2 điểm chọn.";
                    if (soFloorPlanTaoMoi > 0)
                        msg += $"\nĐã tạo {soFloorPlanTaoMoi} Floor Plan mới từ Excel.";
                    if (soScopeBoxGan > 0)
                        msg += $"\nĐã gán Scope Box cho {soScopeBoxGan} Floor Plan.";
                    if (soViewTemplateGan > 0)
                        msg += $"\nĐã gán View Template cho {soViewTemplateGan} Floor Plan.";
                    MessageBox.Show(msg, "Kết quả");
                    Close();
                }
                catch (Exception ex)
                {
                    tg.RollBack();
                    MessageBox.Show($"Có lỗi xảy ra: {ex.Message}\nMọi thay đổi đã được hủy.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void AdjustViewportTitle(Viewport viewport, string titleOnSheet)
        {
            Autodesk.Revit.DB.Outline boxOutline = viewport.GetBoxOutline();
            double vTp_x = (boxOutline.MaximumPoint.X - boxOutline.MinimumPoint.X) / 2;

            double titleLengthInMm = 0;
            if (!string.IsNullOrWhiteSpace(titleOnSheet))
                titleLengthInMm = titleOnSheet.Length * 3.5;
            double titleLengthInFeet = titleLengthInMm / 304.8;

            viewport.LabelLineLength = titleLengthInFeet + 0.0033;
            viewport.LabelOffset = new XYZ(vTp_x - (titleLengthInFeet / 2), -0.03, 0);
        }

        private bool IsSheetNumberExists(string sheetNumber)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .Any(s => s.SheetNumber.Equals(sheetNumber, StringComparison.OrdinalIgnoreCase));
        }

        private void CloseExcelFile(string filePath)
        {
            Excel.Application app = new Excel.Application();
            if (app != null)
            {
                foreach (Workbook workbook in app.Workbooks)
                {
                    if (workbook.FullName == filePath)
                    {
                        workbook.Close(false);
                        break;
                    }
                }
                Marshal.ReleaseComObject(app);
            }
        }

        private Autodesk.Revit.DB.View CreateOrGetView(string viewName, string levelName)
        {
            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(Autodesk.Revit.DB.View))
                .Cast<Autodesk.Revit.DB.View>()
                .FirstOrDefault(v => v.Name == viewName && !v.IsTemplate);

            if (view != null)
                return view;

            if (!string.IsNullOrEmpty(levelName))
            {
                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));
                if (level == null) return null;

                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.FloorPlan);
                if (vft == null) return null;

                string floorPlanName = viewName;
                int suffix = 1;
                string originalName = floorPlanName;
                while (new FilteredElementCollector(doc)
                    .OfClass(typeof(Autodesk.Revit.DB.View))
                    .Cast<Autodesk.Revit.DB.View>()
                    .Any(v => v.Name.Equals(floorPlanName, StringComparison.OrdinalIgnoreCase)))
                {
                    floorPlanName = $"{originalName} ({suffix++})";
                }

                var newView = ViewPlan.Create(doc, vft.Id, level.Id);
                if (newView != null)
                {
                    newView.Name = floorPlanName;
                    var scopeBox = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_VolumeOfInterest)
                        .WhereElementIsNotElementType()
                        .FirstOrDefault();

                    if (scopeBox != null)
                    {
                        var param = newView.LookupParameter("Scope Box");
                        if (param != null && !param.IsReadOnly)
                        {
                            param.Set(scopeBox.Id);
                        }
                    }
                }
                return newView;
            }
            else
            {
                var vft = new FilteredElementCollector(doc)
                    .OfClass(typeof(ViewFamilyType))
                    .Cast<ViewFamilyType>()
                    .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);
                if (vft == null) return null;

                var newView = ViewDrafting.Create(doc, vft.Id);
                newView.Name = viewName;
                return newView;
            }
        }

        private Autodesk.Revit.DB.View CreateDraftingView(Document doc, string viewName)
        {
            var vft = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewFamilyType))
                .Cast<ViewFamilyType>()
                .FirstOrDefault(x => x.ViewFamily == ViewFamily.Drafting);

            if (vft == null) return null;

            var draftingView = ViewDrafting.Create(doc, vft.Id);
            draftingView.Name = viewName;

            var textType = new FilteredElementCollector(doc)
                .OfClass(typeof(TextNoteType))
                .FirstElement() as TextNoteType;

            if (textType != null)
            {
                TextNote.Create(doc, draftingView.Id, new XYZ(0, 0, 0), "DRAFT", textType.Id);
            }

            XYZ startPoint = new XYZ(-0.5, 0, 0);
            XYZ endPoint = new XYZ(0.5, 0, 0);
            Autodesk.Revit.DB.Line line = Autodesk.Revit.DB.Line.CreateBound(startPoint, endPoint);
            doc.Create.NewDetailCurve(draftingView, line);

            return draftingView;
        }

        private void SetParameters(ViewSheet vs, string parameterName, string parameterValue)
        {
            try
            {
                Autodesk.Revit.DB.Parameter p = vs.LookupParameter(parameterName);
                if (p != null && !p.IsReadOnly)
                {
                    var paraType = p.StorageType;
                    if (paraType == StorageType.Integer)
                    {
                        p.Set(int.Parse(parameterValue));
                    }
                    else if (paraType == StorageType.Double)
                    {
                        p.Set(double.Parse(parameterValue));
                    }
                    else if (paraType == StorageType.String)
                    {
                        p.Set(parameterValue);
                    }
                }
            }
            catch { }
        }

        private Tuple<XYZ, XYZ> PickRectangle(UIDocument uidoc)
        {
            try
            {
                XYZ pt1 = uidoc.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm đầu của hình chữ nhật (theo Title Block đã chọn)");
                XYZ pt2 = uidoc.Selection.PickPoint(ObjectSnapTypes.None, "Chọn điểm đối diện của hình chữ nhật");
                return new Tuple<XYZ, XYZ>(pt1, pt2);
            }
            catch
            {
                return null;
            }
        }

        private void cbb_TitleBlocks_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }

        private void AdjustGrids(Document doc, Viewport viewport)
        {
            View view = doc.GetElement(viewport.ViewId) as View;
            if (view == null || view.ViewType != ViewType.FloorPlan) return;

            BoundingBoxXYZ cropBox = view.CropBox;
            var allGrids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>().ToList();

            double offset = 300.0 / 304.8;
            XYZ cropMin = cropBox.Min;
            XYZ cropMax = cropBox.Max;

            double cropLeftX = cropMin.X;
            double cropRightX = cropMax.X;
            double cropTopY = cropMax.Y;
            double cropBottomY = cropMin.Y;

            foreach (Grid grid in allGrids)
            {
                var gridCurves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                foreach (Curve curve in gridCurves)
                {
                    XYZ start = curve.GetEndPoint(0);
                    XYZ end = curve.GetEndPoint(1);

                    double deltaX = Math.Abs(start.X - end.X);
                    double deltaY = Math.Abs(start.Y - end.Y);

                    XYZ newStart, newEnd;

                    if (deltaX > deltaY)
                    {
                        newStart = new XYZ(cropLeftX - offset, start.Y, start.Z);
                        newEnd = new XYZ(cropRightX + offset, end.Y, end.Z);
                    }
                    else
                    {
                        newStart = new XYZ(start.X, cropBottomY - offset, start.Z);
                        newEnd = new XYZ(end.X, cropTopY + offset, end.Z);
                    }

                    Autodesk.Revit.DB.Line newLine = Autodesk.Revit.DB.Line.CreateBound(newStart, newEnd);
                    grid.SetCurveInView(DatumExtentType.ViewSpecific, view, newLine);
                }
            }
        }
    }
}
