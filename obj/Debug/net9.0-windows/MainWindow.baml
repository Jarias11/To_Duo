   M S B A M L   `   `   `  ����C  ?TaskMate, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null9!clr-namespace:TaskMate.ConvertersTaskMate.Converters  5      9!clr-namespace:TaskMate.ViewModelsTaskMate.ViewModels  5          TaskMate.MainWindow   5   	   W SPresentationCore, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35e aSystem.Windows.Controls.Ribbon, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089R NWindowsBase, Version=9.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35F 9http://schemas.microsoft.com/winfx/2006/xaml/presentation     5      R NSystem.Xaml, Version=9.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5      2conv!clr-namespace:TaskMate.Converters     5      3local!clr-namespace:TaskMate.ViewModels     5        � Title$  TaskMate��5      $	��600��6   $	��900��6#   ���#f2f2f2 � 	Resources 5      7   1TaskMate.Converters.AssigneeToVisibilityConverter !  AssigneeToVisibilityConverter8   2TaskMate.ViewModels.BoolToTextDecorationsConverter " BoolToTextDecorationsConverter �� 
TargetType �� Property �� Value(  "System.Windows.Controls.DatePicker��%�  &	        &	      '�� &     '�� "    '  �      5	   
   6-     5
   
   6/   �� 5   
   " ��6   .�5      ��  � ��$ 5q�6'   �� 5       � ��$ 5q�6(   �� 5       � ��$ 14(�6)   �� 5       � �� �#ccc6,   �� 5       � ��$ 1q�60   �� 5       � ��$ 28��6'   5      �� 5   
   " ��6   .�5      ��  � ��$ 5q�6'   �� 5       � ��$ 4q�6(   �� 5       � ��$ 14(�6)   �� 5       � ��$ 28��6'   5      �� 5   
   "  6   .�5      ��  � ��$ 5q�6'   �� 5       � ��$ 14(�6)   �� 5       � ��$ 28��6'   5       5!      .��5#      � $��10q�6    � RowDefinitions 5$   
   �� 5%      $
��Auto �6   �� 5&      $��* �6   �� 5'      $
��Auto �6   .+�5+   
   �� $��0��6   $��0,0,0,10q�6"   .+�5,      ��  �� Orientation$ 
Horizontal=�6   $��	10,0,10,0q�62   .+�5-      �� $��Partner Code:��6   $��Center=�61   �� 5.      $	��200��6   $	��5,0q�6f   ��6&   �� *	PartnerId+ �� UpdateSourceTrigger$ PropertyChanged=�6f   �� 5/      $��Save Partner ID��6   $	��5,0q�6Z   ��63   �� *SavePartnerCommand+6Z   50      �� 52      $ 
Horizontal=�6   $��	10,0,10,0q�62   .+�53      �� $��
Your Code:��6   $��Center=�6.   �� 54      $
��Bold"�64   $	��5,0q�6F   ��6   �� *UserId+6F   55      56      �� 5:   
   $��0��6   
������� �� CornerRadius$ 8��61   	 �� Padding$	 10q�6B   $��	0,70,0,10q�6O   ���#ddd$��1q�6u   ._�5;      �� $ 
Horizontal=�6   $��Center=�62   .+�5<      �� .+�5=      �� $��Title��6    $��5,0,5,0q�6-   �� 5>      $	��180��6   ��6*   �� *NewTaskTitle+5?      �� 5@      .+�5A      �� $��Description��6    $��5,0,5,0q�63   �� 5B      $	��180��6g   
 � ToolTip$
 Optional task description��5C      ��5B      �� *NewTaskDescription+$ PropertyChanged=�5C      5D      �� 5F      .+�5G      �� $��Category��6    $��5,0,5,0q�60   �� 5H      $	��100��6   ��6+   �� *
Categories+ �� SelectedItem 6N   �� *NewTaskCategory+5I      �� 5K      .+�5L      �� $��Due Date��6    $��5,0,5,0q�60     5M      $	��130��6!      SelectedDate 6-   �� *NewTaskDueDate+5N      �� 5P      .+�5Q      �� $��	Assign To��6    $��5,0,5,0q�61   �� 5R      $	��100��6    6+   �� *NewTaskAssignee+.@�5S      �� $��Me��6'   �� 5T      $��Partner��6'   5U      5V      �� 5X      $��Add Task��6   $	��100��6,   $��	10,15,0,0q�68   ��6K   �� *AddTaskCommand+5Y      5Z      � 5]   
   $��1��6    � ColumnDefinitions 5^      �� 5_      $��* �6#   �� 5`      $��* �6#   .+�5d      �� $��0��6   $��5q�6!   ���#E3F2FD.+�5e   
   �� $��My Tasks��6   $��16(�6$   $
��Bold"�62   $��5q�6D   � 5f   
    5g      �� 5h      $
��Auto �6    �� 5i      $��* �6    .+�5m      � $��0��6   ���#e0e0e0$��4q�65    5n      �� 5o      $��30 �6'   �� 5p      $��* �6'   �� 5q      $	��120 �6'   �� 5r      $	��120 �6'   �� 5s      $��40 �6'   .+�5u      �� $	��✓��6   $��0��6%   $
��Bold"�65   �� 5v      $
��Task��6   $��1��6(   $
��Bold"�68   �� 5w      $��Category��6   $��2��6,   $
��Bold"�6<   �� 5x      $	��Due��6   $��3��6'   $
��Bold"�67   �� 5y      $	��❌��6   $��4��6%   $
��Bold"�65   $��Center=�6G   5z      �� 5}      $��1��6   .��5~      �� ��6   �� *Tasks+��5   
   �� 5�      .R�5�      � $��2q�6   
 5�      �� *Description+y�5�      �� *
AssignedTo+ �� 	Converter# [   �� ConverterParameter$ Me�� 5�      �� 5�      $��30 �6+   �� 5�      $��* �6+   �� 5�      $	��120 �6+   �� 5�      $	��120 �6+   �� 5�      $��40 �6+   .+�5�      �� -   5        $��2q�5�   A    d� 	IsChecked 6   �� *IsCompleted+6g   �� 5�      $��1��6    $��Center=�5�       ��5�   0   �� *Title+��5�       �� *IsCompleted+# [ ��5�      �� 5�      " ��6$    �� Triggers 5�   "   �� 5�   &    �� Binding 62   �� *
AssignedTo+ �� Value Me6Q   .a�5�   *   ��  � ��
 � � �6G   5�   '   �� 5�   &    62   �� *
AssignedTo+ Partner6Q   .a�5�   *   ��  � ��
 �� ��6G   5�   '   5�      5�      �� 5�      $��2��6    $��Center=�6J   ��60   �� *
Category+6J   �� 5�      $��3��6    $��Center=�6Y   ��60   �� *	DueDate+ �� StringFormat$ d��6Y   �� 5�      $��4��6   $	��✕��6-   $��30��69   $��25��6D   $��2q�6P   ��5�      �� *DataContext.DeleteTaskCommand+ �� RelativeSource ��  �� AncestorType
 Window��5�      �� 5�      5�      5�      5�      5�      5�      �� 5�      $��1��6   $��5q�6!   ���#F3E5F5.+�5�   
   �� $��Partner's Tasks��6   $��16(�6+   $
��Bold"�69   $��5q�6K   � 5�   
    5�      �� 5�      $
��Auto �6    �� 5�      $��* �6    .+�5�      � $��0��6   ���#e0e0e0$��4q�65    5�      �� 5�      $��30 �6'   �� 5�      $��* �6'   �� 5�      $	��120 �6'   �� 5�      $	��120 �6'   �� 5�      $��40 �6'   .+�5�      �� $	��✓��6   $��0��6%   $
��Bold"�65   �� 5�      $
��Task��6   $��1��6(   $
��Bold"�68   �� 5�      $��Category��6   $��2��6,   $
��Bold"�6<   �� 5�      $	��Due��6   $��3��6'   $
��Bold"�67   �� 5�      $	��❌��6   $��4��6%   $
��Bold"�65   $��Center=�6G   5�      �� 5�      $��1��6   .��5�      �� ��6   �� *Tasks+��5�   
   �� 5�      .R�5�      � $��2q�6   
 5�      �� *Description+y�5�      �� *
AssignedTo+# [  $ Partner�� 5�      �� 5�      $��30 �6+   �� 5�      $��* �6+   �� 5�      $	��120 �6+   �� 5�      $	��120 �6+   �� 5�      $��40 �6+   .+�5�      �� -   5        $��2q�5�   A    6   �� *IsCompleted+6g   �� 5�      $��1��6    $��Center=�5�       ��5�   0   �� *Title+��5�       �� *IsCompleted+# [ ��5�      �� 5�      " ��6$    5�   "   �� 5�   &    62   �� *
AssignedTo+ Me6Q   .a�5�   *   ��  � ��
 � � �6G   5�   '   �� 5�   &    62   �� *
AssignedTo+ Partner6Q   .a�5�   *   ��  � ��
 �� ��6G   5�   '   5�      5�      �� 5�      $��2��6    $��Center=�6J   ��60   �� *
Category+6J   �� 5�      $��3��6    $��Center=�6Y   ��60   �� *	DueDate+$ d��6Y   �� 5�      $��4��6   $	��✕��6-   $��30��69   $��25��6D   $��2q�6P   ��5�      �� *DataContext.DeleteTaskCommand+ �� 
 Window��5�      �� 5�      5�      5�      5�      5�      5�      5�      �� 5�   
   $��2��6   $��5q�6"   ���#FFFDE7.+�5�      �� $��Pending Partner Tasks��6   $��16(�6-   $
��Bold"�6;   $��5q�6M   �� 5�      ��6   �� *PendingTasks+��5�   
   �� 5�      .R�5�      �� ���#E0E0E0$��1q�6/   $ 5��6C   $��5q�6T   $	 5q�6_   
�������._�5�      �� $ 
Horizontal=�6!   $��Center=�6:   .+�5�      �� $��5q�6;   $��Center=�6F   ��6$   �� *Title+6F   �� 5      $��5q�6>   $��Center=�6I   ��6$   �� *
Category+6I   �� 5     $��Accept��6!   $��5q�5  !   ��5  !   �� *DataContext.AcceptTaskCommand+ �� 
 Window��5  !   �� 5  !   5     5     5     5	     5
     5     5     