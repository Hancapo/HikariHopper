import QtQuick

FlatComboBox {
    valueRole: "value"
    model: [
        { label: qsTr("Mitchell filter"), value: "mitchell" },
        { label: qsTr("Catmull-Rom filter"), value: "catmull-rom" },
        { label: qsTr("Triangle filter"), value: "triangle" },
        { label: qsTr("Box filter"), value: "box" },
        { label: qsTr("Cubic B-spline filter"), value: "cubic-bspline" },
        { label: qsTr("Point filter"), value: "point" }
    ]
}
